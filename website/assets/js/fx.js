// Quiet page motion. The page works the same without it.
(() => {
  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  document.documentElement.classList.add("js");

  // Reveal sections once as they scroll in
  const revealer = new IntersectionObserver((entries) => {
    for (const e of entries) if (e.isIntersecting) { e.target.classList.add("in"); revealer.unobserve(e.target); }
  }, { rootMargin: "0px 0px -8% 0px" });
  document.querySelectorAll(".reveal").forEach((el) => revealer.observe(el));

  // The recording only plays while it is on screen, so the page stays light
  const video = document.querySelector(".film video");
  if (video && !reduce) {
    new IntersectionObserver(([e]) => { e.isIntersecting ? video.play().catch(() => {}) : video.pause(); }, { threshold: 0.25 }).observe(video);
  }
})();
