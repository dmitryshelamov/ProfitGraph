// Creates or drops the disposable end-to-end database. Only the test PostgreSQL is accepted, never a development one.
import pg from 'pg';

const [action, name] = process.argv.slice(2);
const server =
  process.env.PROFITGRAPH_E2E_POSTGRES ?? 'postgres://postgres:postgres@localhost:5436/postgres';
const port = new URL(server).port;

if (port !== '5436') {
  throw new Error(
    `End-to-end tests run only against the test PostgreSQL on port 5436, not ${port}.`,
  );
}

if (!/^profitgraph_e2e_[a-f0-9]+$/.test(name ?? '')) {
  throw new Error(
    `Refusing to touch database "${name}": only profitgraph_e2e_<hex> databases are disposable.`,
  );
}

const client = new pg.Client({ connectionString: server });
await client.connect();

try {
  if (action === 'create') {
    await client.query(`CREATE DATABASE ${name}`);
  } else if (action === 'drop') {
    await client.query(`DROP DATABASE IF EXISTS ${name} WITH (FORCE)`);
  } else {
    throw new Error(`Unknown action "${action}"; use create or drop.`);
  }
} finally {
  await client.end();
}
