// POST /api/feedback
// Stores one optional survey answer. No cookies, no names, no emails, and the IP address is never stored.
//
// Storage: a Supabase table (see supabase/feedback.sql), reached with two environment variables
// set in Vercel. Until they are set, this returns 503 and the page says it could not send.
//   SUPABASE_URL                like https://xxxx.supabase.co
//   SUPABASE_SERVICE_ROLE_KEY   server only, never sent to the browser

const ANSWERS = {
  uninstall: ["got_in_the_way", "app_not_working", "did_not_use", "slow", "found_other", "privacy", "reinstalling", "other"],
  heard: ["instagram", "tiktok", "youtube", "reddit", "discord", "friend", "search", "github", "other"],
};

// Light abuse guard: at most 8 posts per minute per address, kept only in memory and never written anywhere
const hits = new Map();
function tooMany(key) {
  const now = Date.now();
  const recent = (hits.get(key) || []).filter((t) => now - t < 60_000);
  recent.push(now);
  hits.set(key, recent);
  if (hits.size > 5000) hits.clear();
  return recent.length > 8;
}

module.exports = async (req, res) => {
  res.setHeader("Cache-Control", "no-store");
  if (req.method !== "POST") { res.setHeader("Allow", "POST"); return res.status(405).json({ error: "Use POST." }); }

  const ip = String(req.headers["x-forwarded-for"] || "").split(",")[0].trim();
  if (tooMany(ip)) return res.status(429).json({ error: "Too many requests. Try again in a minute." });

  let body = req.body;
  if (typeof body === "string") { try { body = JSON.parse(body); } catch { body = null; } }
  if (!body || typeof body !== "object") return res.status(400).json({ error: "Send JSON." });

  // Bots fill the hidden field. Pretend it worked and store nothing.
  if (body.website) return res.status(200).json({ ok: true });

  const kind = String(body.kind || "");
  const answer = String(body.answer || "");
  if (!ANSWERS[kind] || !ANSWERS[kind].includes(answer)) return res.status(400).json({ error: "Unknown answer." });

  // Plain text only, no control characters, at most 500 characters
  const note = String(body.note || "").replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/g, "").trim().slice(0, 500);
  const version = String(body.version || "").replace(/[^0-9.]/g, "").slice(0, 20);

  const url = process.env.SUPABASE_URL, key = process.env.SUPABASE_SERVICE_ROLE_KEY;
  if (!url || !key) return res.status(503).json({ error: "Feedback storage is not set up yet." });

  try {
    const r = await fetch(`${url.replace(/\/$/, "")}/rest/v1/emoji_feedback`, {
      method: "POST",
      headers: { apikey: key, Authorization: `Bearer ${key}`, "Content-Type": "application/json", Prefer: "return=minimal" },
      body: JSON.stringify({ kind, answer, note: note || null, version: version || null }),
    });
    if (!r.ok) {
      console.error("feedback insert failed", r.status); // status only, never the answer text
      return res.status(502).json({ error: "Could not save right now." });
    }
    return res.status(200).json({ ok: true });
  } catch {
    console.error("feedback insert error");
    return res.status(502).json({ error: "Could not save right now." });
  }
};
