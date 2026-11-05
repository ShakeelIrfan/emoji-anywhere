// Page motion. Everything here is decoration: the page works the same without it.
(() => {
  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const finePointer = window.matchMedia("(pointer: fine)").matches;
  document.documentElement.classList.add("js");

  // Reveal sections once as they scroll in. Things already on screen show right away.
  const revealer = new IntersectionObserver((entries) => {
    for (const e of entries) if (e.isIntersecting) { e.target.classList.add("in"); revealer.unobserve(e.target); }
  }, { rootMargin: "0px 0px -8% 0px" });
  document.querySelectorAll(".reveal, .screen").forEach((el) => revealer.observe(el));

  // Pause looping animations and the video while they are off screen, so the page stays light
  const pauser = new IntersectionObserver((entries) => {
    for (const e of entries) {
      e.target.classList.toggle("paused", !e.isIntersecting);
      const v = e.target.querySelector("video");
      if (v && !reduce) e.isIntersecting ? v.play().catch(() => {}) : v.pause();
    }
  });
  document.querySelectorAll(".hero, .marquee, .screen").forEach((el) => pauser.observe(el));

  // 3D tilt: the demo card leans toward the pointer and the floating emojis drift by depth
  const stage = document.querySelector(".stage");
  const tilt = stage && stage.querySelector(".tilt");
  if (!tilt || reduce || !finePointer) return;
  const area = document.querySelector(".hero");
  let frame = 0, rx = 0, ry = 0;
  area.addEventListener("pointermove", (ev) => {
    const r = stage.getBoundingClientRect();
    const x = (ev.clientX - (r.left + r.width / 2)) / r.width;
    const y = (ev.clientY - (r.top + r.height / 2)) / r.height;
    ry = Math.max(-1, Math.min(1, x)) * 10;
    rx = Math.max(-1, Math.min(1, y)) * -8;
    if (!frame) frame = requestAnimationFrame(() => {
      frame = 0;
      tilt.style.transform = `rotateX(${rx.toFixed(2)}deg) rotateY(${ry.toFixed(2)}deg)`;
    });
  });
  area.addEventListener("pointerleave", () => { tilt.style.transform = ""; });
})();
