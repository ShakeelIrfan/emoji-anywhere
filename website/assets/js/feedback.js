// Optional feedback: "where did you hear about us" after a download, and "why did you uninstall".
// Sends only the chosen answer, an optional note and the app version. No cookies, no names, no emails.
(() => {
  const heard = document.getElementById("heard");
  if (heard) {
    // The download keeps going; this just appears under the button
    document.querySelectorAll('a[href*="/releases/latest/download/"]').forEach((a) =>
      a.addEventListener("click", () => { heard.hidden = false; }));
  }

  const version = (new URLSearchParams(location.search).get("v") || "").replace(/[^0-9.]/g, "").slice(0, 20);

  document.querySelectorAll("form.survey").forEach((form) => {
    const status = form.querySelector(".status");
    let sent = false;

    async function send() {
      if (sent) return;
      const data = new FormData(form);
      const answer = data.get("answer");
      if (!answer) { status.textContent = "Pick one of the options first."; return; }
      const button = form.querySelector('button[type="submit"]');
      if (button) button.disabled = true;
      status.classList.remove("ok");
      status.textContent = "Sending…";
      try {
        const res = await fetch("/api/feedback", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            kind: form.dataset.kind,
            answer,
            note: (data.get("note") || "").toString().slice(0, 500),
            version,
            website: data.get("website") || "",
          }),
        });
        if (!res.ok) throw new Error(String(res.status));
        sent = true;
        status.classList.add("ok");
        status.textContent = form.dataset.kind === "heard" ? "Thanks! Enjoy the app 🙌" : "Thank you. This really helps.";
        form.querySelectorAll("input, textarea, button").forEach((el) => { el.disabled = true; });
      } catch {
        status.textContent = "That didn't send. You can try again in a moment.";
        if (button) button.disabled = false;
      }
    }

    form.addEventListener("submit", (ev) => { ev.preventDefault(); send(); });
    // One tap is enough for the short "where did you hear" question
    if (form.hasAttribute("data-autosend")) form.addEventListener("change", (ev) => { if (ev.target.name === "answer") send(); });
  });
})();
