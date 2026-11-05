-- Optional survey answers from the website. No names, emails or IP addresses.
-- Run once in the Supabase SQL editor of the project used for feedback.

create table if not exists public.emoji_feedback (
  id bigint generated always as identity primary key,
  created_at timestamptz not null default now(),
  kind text not null check (kind in ('uninstall', 'heard')),
  answer text not null check (char_length(answer) <= 40),
  note text check (char_length(note) <= 500),
  version text check (char_length(version) <= 20)
);

-- Locked down: no public access at all. Only the server function (service role key) can insert,
-- and only you can read it from the Supabase dashboard.
alter table public.emoji_feedback enable row level security;

-- Handy summary views for the dashboard
create or replace view public.emoji_feedback_summary as
  select kind, answer, count(*) as total, max(created_at) as latest
  from public.emoji_feedback group by kind, answer order by kind, total desc;
revoke all on public.emoji_feedback_summary from anon, authenticated;
