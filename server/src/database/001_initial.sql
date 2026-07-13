CREATE TABLE IF NOT EXISTS schema_migrations (name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS users (
  id uuid PRIMARY KEY, email text NOT NULL, password_hash text NOT NULL, created_at timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT users_email_lower CHECK (email = lower(email)), UNIQUE(email)
);
CREATE TABLE IF NOT EXISTS refresh_sessions (
  id uuid PRIMARY KEY, user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  token_hash text NOT NULL UNIQUE, device_name text NOT NULL DEFAULT '', expires_at timestamptz NOT NULL,
  revoked_at timestamptz, created_at timestamptz NOT NULL DEFAULT now()
);
CREATE SEQUENCE IF NOT EXISTS sync_cursor_seq;
CREATE TABLE IF NOT EXISTS todos (
  user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE, id uuid NOT NULL,
  date date NOT NULL, title text NOT NULL, time time, notes text NOT NULL DEFAULT '', completed boolean NOT NULL DEFAULT false,
  deleted boolean NOT NULL DEFAULT false, revision bigint NOT NULL DEFAULT 1,
  cursor bigint NOT NULL DEFAULT nextval('sync_cursor_seq'), updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY(user_id, id)
);
CREATE INDEX IF NOT EXISTS todos_sync_idx ON todos(user_id, cursor);
