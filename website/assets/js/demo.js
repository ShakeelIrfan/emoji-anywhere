// Live demo: the same idea as the app, running in this text box.
// Type ":" and two letters, pick with arrows, Enter or Tab, or finish the code with ":".
(() => {
  const input = document.getElementById("try");
  const list = document.getElementById("pop");
  const measure = document.getElementById("measure");
  const out = document.getElementById("out");
  if (!input || !list || !window.EMOJI_DATA) return;
  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  // Same ranking idea as the app: exact code, start of code, tag, then a nudge for popular ones
  const popular = new Set("😂❤️🤣👍😭🙏😘🥰😍😊🎉😁💕🥺😅🔥🤔👏🙂😎👌💜😔💪✨💖👀😋😢💯😩😡😜🙈🤞😄🙌🤪😀💀💔😌🤩🙃😬😱😴😇😈😞🖤💩✅👋🤮😤🤢🌟🌈🤯💃👊🤬😕⚡☕⭐🤨🤘🤝🫡🫠".match(/\p{Extended_Pictographic}️?/gu));
  const byCode = new Map();
  for (const e of window.EMOJI_DATA) for (const c of e[1]) if (!byCode.has(c)) byCode.set(c, e);

  function score(e, q) {
    let best = 0;
    for (const c of e[1]) {
      if (c === q) return 1000;
      if (c.startsWith(q)) best = Math.max(best, 600 - c.length);
      else if (c.includes("_" + q)) best = Math.max(best, 400 - c.length);
    }
    for (const t of e[2]) {
      if (t === q) best = Math.max(best, 590 - e[1][0].length / 10);
      else if (t.startsWith(q)) best = Math.max(best, 250);
    }
    return best ? best + (popular.has(e[0]) ? 30 : 0) : 0;
  }

  function search(q) {
    const scored = [];
    for (const e of window.EMOJI_DATA) { const s = score(e, q); if (s) scored.push([s, e]); }
    return scored.sort((a, b) => b[0] - a[0]).slice(0, 6).map((x) => x[1]);
  }

  const word = (e) => e[1].find((c) => /^[a-z]/.test(c)) || e[1][0];
  let results = [], selected = 0, start = -1;

  // ":" counts only at the start or after a space, like in the app
  function query() {
    const caret = input.selectionStart, text = input.value.slice(0, caret);
    const m = text.match(/(^|\s):([a-z0-9_+-]*)$/i);
    if (!m) return null;
    start = caret - m[2].length - 1;
    return m[2].toLowerCase();
  }

  function render() {
    list.innerHTML = "";
    results.forEach((e, i) => {
      const li = document.createElement("li");
      li.id = "opt-" + i;
      li.setAttribute("role", "option");
      li.setAttribute("aria-selected", String(i === selected));
      const glyph = document.createElement("span");
      glyph.className = "e";
      glyph.setAttribute("aria-hidden", "true");
      glyph.textContent = e[0];
      const code = document.createElement("span");
      code.textContent = `:${word(e)}:`;
      li.append(glyph, code);
      li.addEventListener("mousedown", (ev) => { ev.preventDefault(); pick(e); });
      list.appendChild(li);
    });
    input.setAttribute("aria-activedescendant", results.length ? "opt-" + selected : "");
  }

  function place() {
    measure.textContent = input.value.slice(0, input.selectionStart);
    const x = Math.min(measure.offsetWidth - input.scrollLeft + 16, input.clientWidth - 220);
    list.style.left = Math.max(0, x) + "px";
    list.style.top = input.offsetHeight + "px";
  }

  function update() {
    const q = query();
    results = q && q.length >= 2 ? search(q) : [];
    selected = 0;
    if (!results.length) return close();
    render();
    place();
    list.hidden = false;
    input.setAttribute("aria-expanded", "true");
  }

  function close() {
    list.hidden = true;
    results = [];
    input.setAttribute("aria-expanded", "false");
    input.removeAttribute("aria-activedescendant");
  }

  // A quick pop of the emoji above the cursor, so you see what just happened
  function burst(emoji) {
    if (reduceMotion) return;
    measure.textContent = input.value.slice(0, input.selectionStart);
    const b = document.createElement("span");
    b.className = "burst";
    b.setAttribute("aria-hidden", "true");
    b.textContent = emoji;
    b.style.left = Math.min(measure.offsetWidth - input.scrollLeft + 6, input.clientWidth - 20) + "px";
    b.style.top = "0px";
    input.parentElement.appendChild(b);
    b.addEventListener("animationend", () => b.remove());
  }

  function pick(e, keepFocus = true) {
    const caret = input.selectionStart;
    input.setRangeText(e[0], start, caret, "end");
    burst(e[0]);
    close();
    out.textContent = `Inserted ${e[0]} for :${word(e)}:`;
    if (keepFocus) input.focus();
  }

  input.addEventListener("input", () => {
    // A finished code like :fire: converts right away
    const caret = input.selectionStart, text = input.value.slice(0, caret);
    const m = text.match(/(^|\s):([a-z0-9_+-]+):$/i);
    const hit = m && byCode.get(m[2].toLowerCase());
    if (hit) {
      input.setRangeText(hit[0], caret - m[2].length - 2, caret, "end");
      burst(hit[0]);
      out.textContent = `Inserted ${hit[0]} for :${m[2].toLowerCase()}:`;
      return close();
    }
    update();
  });

  input.addEventListener("keydown", (ev) => {
    if (list.hidden) return;
    if (ev.key === "ArrowDown") { selected = (selected + 1) % results.length; render(); ev.preventDefault(); }
    else if (ev.key === "ArrowUp") { selected = (selected - 1 + results.length) % results.length; render(); ev.preventDefault(); }
    else if (ev.key === "Enter" || ev.key === "Tab") { pick(results[selected]); ev.preventDefault(); }
    else if (ev.key === "Escape") { close(); ev.preventDefault(); }
  });
  input.addEventListener("blur", () => setTimeout(close, 100));
  input.addEventListener("click", update);

  // Until someone clicks in, show a few examples, then stop. Skipped for reduced motion.
  if (reduceMotion) return;
  let touched = false;
  input.addEventListener("focus", () => { touched = true; });
  // Each example: text to type, then "pick" (choose from the popup) or "done" (a full code converts by itself)
  const examples = [["nice work :fir", "pick"], ["monday again :sob:", "done"], ["ship it :rocket:", "done"], ["that meme :lol", "pick"]];
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  (async () => {
    await wait(1200);
    for (const [text, how] of examples) {
      if (touched) return;
      input.value = "";
      for (const ch of text) {
        if (touched) return;
        input.value += ch;
        input.setSelectionRange(input.value.length, input.value.length);
        input.dispatchEvent(new Event("input"));
        await wait(85 + Math.random() * 60);
      }
      if (how === "pick") { await wait(800); if (touched || !results.length) return; pick(results[0], false); }
      await wait(1700);
    }
    if (!touched) out.textContent = "Your turn. Click the box and try :sad or :skull";
  })();
})();
