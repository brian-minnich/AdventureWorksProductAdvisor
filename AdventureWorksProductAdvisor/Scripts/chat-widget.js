(function () {
    "use strict";

    function getSelectedMode() {
        var radios = document.getElementsByName("mode");
        for (var i = 0; i < radios.length; i++) {
            if (radios[i].checked) {
                return radios[i].value;
            }
        }
        return "CSharp";
    }

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
        bubble.innerHTML = "<strong>[" + mode + "]</strong> " + text;
        history.appendChild(bubble);
    }

    function askQuestion() {
        var question = document.getElementById("question").value.trim();
        if (!question) {
            return;
        }

        var mode = getSelectedMode();
        var topN = parseInt(document.getElementById("topN").value, 10);

        var askButton = document.getElementById("askButton");
        askButton.disabled = true;

        fetch("/api/ask", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ Question: question, Mode: mode, TopN: topN })
        })
            .then(function (response) {
                return response.json();
            })
            .then(function (data) {
                if (data.Success) {
                    appendBubble(data.Mode, data.Answer, false);
                } else {
                    appendBubble(data.Mode || mode, data.ErrorMessage, true);
                }
            })
            .catch(function () {
                appendBubble(mode, "Something went wrong contacting the server.", true);
            })
            .then(function () {
                askButton.disabled = false;
            });
    }

    document.addEventListener("DOMContentLoaded", function () {
        document.getElementById("askButton").addEventListener("click", askQuestion);
    });
})();
