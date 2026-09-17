// Self-contained widget: everything lives inside this one function
// ((function(){...})()  is called an IIFE - Immediately Invoked Function
// Expression), so none of the helper functions below leak out as global
// variables that some other script could accidentally collide with.
(function () {
    "use strict";

    // Explicit allowlist for rendering LLM answers, rather than trusting
    // DOMPurify's full default profile. Covers what the system prompt's
    // markdown (bold, bullets, paragraphs) actually produces plus a little
    // headroom (links, code) - nothing broader. href is the only attribute
    // allowed through, and the hook below forces rel="noopener noreferrer"
    // on any link that makes it through.
    var SANITIZE_CONFIG = {
        ALLOWED_TAGS: ["p", "br", "strong", "em", "ul", "ol", "li", "code", "pre", "a"],
        ALLOWED_ATTR: ["href"]
    };
    DOMPurify.addHook("afterSanitizeAttributes", function (node) {
        if (node.tagName === "A") {
            node.setAttribute("rel", "noopener noreferrer");
        }
    });

    // Short initials shown in the avatar circle - the avatar's color+
    // initials are what identifies which pipeline answered (see
    // .avatar.mode-* in chat-widget.css for the actual colors).
    var AVATAR_TEXT = { CSharp: "C#", StoredProc: "SP" };

    // Reads which radio button (C# Pipeline / Stored Procedure) is
    // currently selected. Falls back to "CSharp" just in case nothing is
    // checked, though the markup always has one checked by default.
    function getSelectedMode() {
        var radios = document.getElementsByName("mode");
        for (var i = 0; i < radios.length; i++) {
            if (radios[i].checked) {
                return radios[i].value;
            }
        }
        return "CSharp";
    }

    // Appends the user's own question as a plain right-aligned bubble (no
    // avatar - the avatar is reserved for identifying which pipeline
    // answered). This is the user's own text, so it's inserted as a plain
    // text node, never parsed as markdown/HTML.
    function appendQuestionBubble(text) {
        var history = document.getElementById("chatHistory");
        var bubble = document.createElement("div");
        bubble.className = "question-bubble";
        bubble.textContent = text;
        history.appendChild(bubble);
    }

    // Appends a "Thinking…" placeholder answer bubble-row and returns
    // references to its avatar and bubble elements, so the fetch callback
    // can update this same row in place once the real answer arrives
    // instead of appending a second row.
    function appendAnswerPlaceholder(mode) {
        var history = document.getElementById("chatHistory");

        var row = document.createElement("div");
        row.className = "bubble-row";

        var avatar = document.createElement("div");
        avatar.className = "avatar " + (mode === "StoredProc" ? "mode-storedproc" : "mode-csharp");
        avatar.textContent = AVATAR_TEXT[mode] || mode.slice(0, 2).toUpperCase();
        row.appendChild(avatar);

        var bubble = document.createElement("div");
        bubble.className = "bubble";
        bubble.textContent = "Thinking…";
        row.appendChild(bubble);

        history.appendChild(row);
        return { avatar: avatar, bubble: bubble };
    }

    // Fills in a placeholder row (see appendAnswerPlaceholder) with the
    // real answer. Error text is a static app string (never LLM output),
    // so it's still inserted as a plain text node. Success answers are raw
    // LLM output formatted as markdown (the system prompt asks for
    // **bold** and "- " bullets) - marked renders that to HTML and
    // DOMPurify sanitizes it before it touches innerHTML, so characters
    // like < or & in review content still can't be interpreted as live
    // HTML/script.
    function updateAnswerBubble(placeholder, mode, text, isError) {
        var avatarClass = isError ? "mode-error" : (mode === "StoredProc" ? "mode-storedproc" : "mode-csharp");
        placeholder.avatar.className = "avatar " + avatarClass;
        placeholder.avatar.textContent = isError ? "!" : (AVATAR_TEXT[mode] || mode.slice(0, 2).toUpperCase());

        placeholder.bubble.className = "bubble" + (isError ? " is-error" : "");
        placeholder.bubble.innerHTML = "";
        if (isError) {
            placeholder.bubble.appendChild(document.createTextNode(text));
        } else {
            placeholder.bubble.innerHTML = DOMPurify.sanitize(marked.parse(text), SANITIZE_CONFIG);
        }
    }

    function scrollHistoryToBottom() {
        var history = document.getElementById("chatHistory");
        history.scrollTop = history.scrollHeight;
    }

    // Reads #chatShell's own CSS transition-duration so the deferred class
    // swap below lands after the fade-out actually finishes - this stays
    // in sync with chat-widget.css automatically (a changed transition
    // duration there just works), instead of a hardcoded duration that has
    // to be kept in sync by hand.
    function getTransitionMs(shell) {
        var seconds = parseFloat(window.getComputedStyle(shell).transitionDuration);
        return (seconds || 0.15) * 1000 + 10;
    }

    // Shared fade-out/mutate/fade-in around a layout-changing DOM mutation
    // - used by both switchToChatView() and newChat(), which are otherwise
    // identical except for what they mutate.
    function fadeShell(shell, mutate) {
        shell.classList.add("is-transitioning");
        window.setTimeout(function () {
            mutate();
            shell.classList.remove("is-transitioning");
        }, getTransitionMs(shell));
    }

    // Switches #chatShell from the single centered panel (no history yet)
    // to the sidebar + scrollable history app shell, once - see the
    // .has-chat rules in chat-widget.css for what actually changes. The
    // fade out/in around the class swap covers up the layout jump (flex
    // direction, sidebar width, etc. can't be smoothly transitioned).
    // #chatHistory is still display:none until has-chat is added, so any
    // scrollHistoryToBottom() call before now was a no-op on a hidden
    // element - this is the first point where scrolling actually does
    // anything, which matters when the first answer arrives before the
    // fade even finishes.
    function switchToChatView() {
        var shell = document.getElementById("chatShell");
        if (shell.classList.contains("has-chat")) {
            return;
        }
        fadeShell(shell, function () {
            shell.classList.add("has-chat");
            scrollHistoryToBottom();
        });
    }

    // Clears the session's history and fades back to the single centered
    // panel - the reverse of switchToChatView(). Does not touch the
    // selected mode or review count, only the conversation itself. Guarded
    // the same way askQuestion() guards against double-submits: while this
    // (or a pending question) is already in progress, askButton stays
    // disabled, so a second New Chat click - or a question submitted
    // during this fade-out - can't run and clear history out from under
    // an in-flight request.
    function newChat() {
        var shell = document.getElementById("chatShell");
        var askButton = document.getElementById("askButton");
        var newChatBtn = document.getElementById("newChatBtn");
        if (!shell.classList.contains("has-chat") || askButton.disabled) {
            return;
        }
        askButton.disabled = true;
        newChatBtn.disabled = true;
        fadeShell(shell, function () {
            document.getElementById("chatHistory").innerHTML = "";
            shell.classList.remove("has-chat");
            askButton.disabled = false;
            newChatBtn.disabled = false;
            document.getElementById("question").focus();
        });
    }

    // Runs when the Ask button is clicked: reads the form, calls the API,
    // and renders whatever comes back.
    function askQuestion() {
        var askButton = document.getElementById("askButton");
        // Guards more than the button's own click: the Enter-key handler
        // below calls askQuestion() directly, which a disabled attribute
        // alone doesn't block, so without this check a held/rapid Enter
        // (or a click landing right as newChat() disables things) could
        // still start a second overlapping request or fire while newChat()
        // is mid-clear.
        if (askButton.disabled) {
            return;
        }

        var questionInput = document.getElementById("question");
        var question = questionInput.value.trim();
        if (!question) {
            return;
        }

        var mode = getSelectedMode();
        var topN = parseInt(document.getElementById("topN").value, 10);
        var newChatBtn = document.getElementById("newChatBtn");

        askButton.disabled = true;
        newChatBtn.disabled = true;
        questionInput.value = "";

        appendQuestionBubble(question);
        var placeholder = appendAnswerPlaceholder(mode);
        scrollHistoryToBottom();
        switchToChatView();

        // The field names here (Question/Mode/TopN) must match AskRequest.cs
        // exactly - Web API's JSON formatter does not remap casing, so a
        // mismatch here would silently fail to bind on the server side with
        // no compiler error to catch it.
        fetch("/api/ask", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ Question: question, Mode: mode, TopN: topN })
        })
            .then(function (response) {
                return response.json();
            })
            .then(function (data) {
                // AskResponse.Success tells us whether the pipeline actually
                // answered, separately from the HTTP status code (which is
                // always 200 here - see AskController).
                if (data.Success) {
                    updateAnswerBubble(placeholder, data.Mode, data.Answer, false);
                } else {
                    updateAnswerBubble(placeholder, data.Mode || mode, data.ErrorMessage, true);
                }
                scrollHistoryToBottom();
            })
            // Covers network failures (server unreachable, etc.), as
            // opposed to the branch above which covers the server
            // responding but with Success: false.
            .catch(function () {
                updateAnswerBubble(placeholder, mode, "Something went wrong contacting the server.", true);
                scrollHistoryToBottom();
            })
            // Runs after either the success/failure branch above OR the
            // catch, so the buttons are always re-enabled no matter how the
            // request ended.
            .then(function () {
                askButton.disabled = false;
                newChatBtn.disabled = false;
            });
    }

    // Nudges the #topN input by +/-1, clamped to its own min/max (1-10) -
    // the same range AskController enforces server-side regardless, so
    // this is just keeping the stepper buttons consistent with the native
    // number input they sit next to.
    function stepTopN(delta) {
        var topN = document.getElementById("topN");
        var next = (parseInt(topN.value, 10) || 0) + delta;
        var min = parseInt(topN.min, 10);
        var max = parseInt(topN.max, 10);
        topN.value = Math.min(max, Math.max(min, next));
    }

    // Wait for the page to finish loading before wiring up event handlers,
    // since the elements don't exist in the DOM until then.
    document.addEventListener("DOMContentLoaded", function () {
        document.getElementById("askButton").addEventListener("click", askQuestion);
        // Enter submits the question, same as clicking Ask. The question
        // input isn't inside a <form>, so Enter does nothing by default -
        // this wires it up explicitly rather than adding a form element
        // just to get free browser submit-on-Enter behavior.
        document.getElementById("question").addEventListener("keydown", function (event) {
            if (event.key === "Enter") {
                askQuestion();
            }
        });
        document.querySelector(".stepper-btn.decrement").addEventListener("click", function () {
            stepTopN(-1);
        });
        document.querySelector(".stepper-btn.increment").addEventListener("click", function () {
            stepTopN(1);
        });
        document.getElementById("newChatBtn").addEventListener("click", newChat);
    });
})();
