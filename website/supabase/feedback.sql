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

-- Last resort backstop against floods, far above real traffic. Hitting it only pauses optional survey
-- answers for a minute; the site and downloads are unaffected. Per-address limits live in the API.
create or replace function public.emoji_feedback_rate_cap() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if (select count(*) from public.emoji_feedback where created_at > now() - interval '1 minute') >= 600 then
    raise exception 'feedback rate cap reached' using errcode = 'P0001';
  end if;
  return new;
end $$;
drop trigger if exists emoji_feedback_rate_cap on public.emoji_feedback;
create trigger emoji_feedback_rate_cap before insert on public.emoji_feedback
  for each row execute function public.emoji_feedback_rate_cap();
create index if not exists emoji_feedback_created_at on public.emoji_feedback (created_at desc);

-- Handy summary views for the dashboard
create or replace view public.emoji_feedback_summary as
  select kind, answer, count(*) as total, max(created_at) as latest
  from public.emoji_feedback group by kind, answer order by kind, total desc;
revoke all on public.emoji_feedback_summary from anon, authenticated;
