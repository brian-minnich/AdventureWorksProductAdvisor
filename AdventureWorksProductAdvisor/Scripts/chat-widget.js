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

    // Removes whatever is currently shown in the history area and returns
    // it, so appendBubble can reuse the same element instead of looking it
    // up twice. Called at the top of appendBubble so only the most recent
    // question's bubble (loading placeholder, answer, or error) is ever on
    // screen at once, rather than piling up one bubble per question asked
    // this session.
    function clearHistory() {
        var history = document.getElementById("chatHistory");
        while (history.firstChild) {
            history.removeChild(history.firstChild);
        }
        return history;
    }

    // Short initials shown in the avatar circle - "[Mode]" text labels are
    // gone from the bubble itself, so the avatar's color+initials are now
    // what identifies which pipeline answered (see .avatar.mode-* in
    // chat-widget.css for the actual colors).
    var AVATAR_TEXT = { CSharp: "C#", StoredProc: "SP" };

    // Replaces the history area with one avatar + message bubble. Error
    // text is a static app string (never LLM output), so it's still
    // inserted as a plain text node. Success answers are raw LLM output
    // formatted as markdown (the system prompt asks for **bold** and "- "
    // bullets) - marked renders that to HTML and DOMPurify sanitizes it
    // before it touches innerHTML, so characters like < or & in review
    // content still can't be interpreted as live HTML/script.
    function appendBubble(mode, text, isError) {
        var history = clearHistory();

        var row = document.createElement("div");
        row.className = "bubble-row";

        var avatarClass = isError ? "mode-error" : (mode === "StoredProc" ? "mode-storedproc" : "mode-csharp");
        var avatar = document.createElement("div");
        avatar.className = "avatar " + avatarClass;
        avatar.textContent = isError ? "!" : (AVATAR_TEXT[mode] || mode.slice(0, 2).toUpperCase());
        row.appendChild(avatar);

        var bubble = document.createElement("div");
        bubble.className = "bubble" + (isError ? " is-error" : "");

        if (isError) {
            bubble.appendChild(document.createTextNode(text));
        } else {
            bubble.innerHTML = DOMPurify.sanitize(marked.parse(text), SANITIZE_CONFIG);
        }
        row.appendChild(bubble);

        history.appendChild(row);
    }

    // Runs when the Ask button is clicked: reads the form, calls the API,
    // and renders whatever comes back.
    function askQuestion() {
        var question = document.getElementById("question").value.trim();
        if (!question) {
            return;
        }

        var mode = getSelectedMode();
        var topN = parseInt(document.getElementById("topN").value, 10);

        var askButton = document.getElementById("askButton");
        askButton.disabled = true;

        // Replace whatever's currently shown with a placeholder right away,
        // rather than leaving the previous answer up during the several
        // seconds the LLM call can take.
        appendBubble(mode, "Thinking…", false);

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
                    appendBubble(data.Mode, data.Answer, false);
                } else {
                    appendBubble(data.Mode || mode, data.ErrorMessage, true);
                }
            })
            // Covers network failures (server unreachable, etc.), as
            // opposed to the branch above which covers the server
            // responding but with Success: false.
            .catch(function () {
                appendBubble(mode, "Something went wrong contacting the server.", true);
            })
            // Runs after either the success/failure branch above OR the
            // catch, so the button is always re-enabled no matter how the
            // request ended.
            .then(function () {
                askButton.disabled = false;
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
    });
})();
