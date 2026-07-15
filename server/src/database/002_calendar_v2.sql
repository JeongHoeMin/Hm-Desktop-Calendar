CREATE SEQUENCE IF NOT EXISTS calendar_sync_cursor_seq;

CREATE TABLE IF NOT EXISTS calendar_items (
  user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  id uuid NOT NULL,
  kind text NOT NULL CHECK (kind IN ('schedule', 'anniversary')),
  title text NOT NULL,
  notes text NOT NULL DEFAULT '',
  start_date date NOT NULL,
  end_date date NOT NULL,
  start_time time,
  end_time time,
  all_day boolean NOT NULL DEFAULT false,
  completed boolean NOT NULL DEFAULT false,
  color text NOT NULL,
  recurrence jsonb,
  reminders jsonb NOT NULL DEFAULT '[]'::jsonb,
  deleted boolean NOT NULL DEFAULT false,
  revision bigint NOT NULL DEFAULT 1,
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY(user_id, id),
  CONSTRAINT calendar_items_date_order CHECK (end_date >= start_date)
);

CREATE TABLE IF NOT EXISTS date_cell_decorations (
  user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  id uuid NOT NULL,
  date date NOT NULL,
  kind text NOT NULL CHECK (kind IN ('highlight', 'colorDot', 'label')),
  color text NOT NULL,
  label text NOT NULL DEFAULT '',
  deleted boolean NOT NULL DEFAULT false,
  revision bigint NOT NULL DEFAULT 1,
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY(user_id, id)
);

CREATE TABLE IF NOT EXISTS calendar_sync_changes (
  user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  cursor bigint PRIMARY KEY DEFAULT nextval('calendar_sync_cursor_seq'),
  entity_type text NOT NULL CHECK (entity_type IN ('calendarItem', 'dateCellDecoration')),
  entity_id uuid NOT NULL,
  payload jsonb NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS calendar_sync_changes_user_cursor_idx
  ON calendar_sync_changes(user_id, cursor);
