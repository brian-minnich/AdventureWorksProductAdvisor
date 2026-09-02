// Self-contained widget: everything lives inside this one function
// ((function(){...})()  is called an IIFE - Immediately Invoked Function
// Expression), so none of the helper functions below leak out as global
// variables that some other script could accidentally collide with.
(function () {
    "use strict";

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

    // Adds one chat bubble to the page. Built with createElement/
    // textContent/createTextNode rather than innerHTML + string
    // concatenation, so that if `text` (which can be raw LLM output built
    // from real review content) ever contains characters like < or &, they
    // display as literal text instead of being interpreted as HTML.
    function appendBubble(mode, text, isError) {
        var history = document.getElementById("chatHistory");
        var bubble = document.createElement("div");
        bubble.style.border = "1px solid #ccc";
        bubble.style.borderRadius = "6px";
        bubble.style.padding = "10px";
        bubble.style.marginBottom = "10px";
        if (isError) {
            bubble.style.borderColor = "#a94442";
            bubble.style.color = "#a94442";
        }
        var label = document.createElement("strong");
        label.textContent = "[" + mode + "]";
        bubble.appendChild(label);
        bubble.appendChild(document.createTextNode(" " + text));
        history.appendChild(bubble);
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

    // Wait for the page to finish loading before wiring up the click
    // handler, since the button doesn't exist in the DOM until then.
    document.addEventListener("DOMContentLoaded", function () {
        document.getElementById("askButton").addEventListener("click", askQuestion);
    });
})();
