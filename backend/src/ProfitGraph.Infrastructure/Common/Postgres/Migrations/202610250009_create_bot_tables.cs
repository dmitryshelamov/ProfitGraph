using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250009)]
public sealed class CreateBotTablesMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE bots (
                id uuid PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES users (id),
                strategy_id uuid NOT NULL REFERENCES strategies (id),
                strategy_version_id uuid NOT NULL REFERENCES strategy_versions (id),
                exchange_credential_id uuid NOT NULL REFERENCES exchange_credentials (id),
                venue varchar(32) NOT NULL,
                symbol varchar(64) NOT NULL,
                timeframe varchar(16) NOT NULL,
                parameters jsonb NOT NULL,
                allocation_cap numeric(38, 18) NOT NULL CHECK (allocation_cap > 0),
                max_order_quote numeric(38, 18) NOT NULL CHECK (max_order_quote > 0),
                daily_loss_limit_quote numeric(38, 18) NOT NULL CHECK (daily_loss_limit_quote > 0),
                limit_fee_rate numeric(38, 18) NOT NULL CHECK (limit_fee_rate >= 0 AND limit_fee_rate < 1),
                market_fee_rate numeric(38, 18) NOT NULL CHECK (market_fee_rate >= 0 AND market_fee_rate < 1),
                fee_rates_read_at timestamp with time zone NOT NULL,
                allow_foreign_orders boolean NOT NULL,
                desired_state varchar(16) NOT NULL CHECK (desired_state IN ('Stopped', 'Running')),
                runtime_status varchar(16) NOT NULL CHECK (runtime_status IN ('Created', 'Starting', 'Running', 'Degraded', 'Stopped')),
                status_reason varchar(32) NULL,
                diagnostics integer NOT NULL CHECK (diagnostics >= 0),
                restart_attempts integer NOT NULL CHECK (restart_attempts BETWEEN 0 AND 5),
                next_restart_at timestamp with time zone NULL,
                has_run boolean NOT NULL,
                first_started_at timestamp with time zone NULL,
                day_start_utc date NULL,
                day_start_equity numeric(38, 18) NULL,
                rejected_version_id uuid NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                CHECK ((day_start_utc IS NULL) = (day_start_equity IS NULL))
            );
            CREATE INDEX bots_user_created ON bots (user_id, created_at DESC, id);
            CREATE INDEX bots_desired_running ON bots (id) WHERE desired_state = 'Running';
            CREATE INDEX bots_exchange_credential ON bots (exchange_credential_id);

            CREATE TABLE bot_leases (
                bot_id uuid PRIMARY KEY REFERENCES bots (id) ON DELETE CASCADE,
                holder_id uuid NOT NULL,
                epoch bigint NOT NULL CHECK (epoch > 0),
                expires_at timestamp with time zone NOT NULL,
                submissions_ready boolean NOT NULL
            );

            CREATE TABLE bot_checkpoints (
                bot_id uuid PRIMARY KEY REFERENCES bots (id) ON DELETE CASCADE,
                schema_version integer NOT NULL CHECK (schema_version > 0),
                checkpoint jsonb NOT NULL,
                applied_parameters jsonb NOT NULL,
                updated_at timestamp with time zone NOT NULL
            );

            CREATE TABLE bot_order_intents (
                id uuid PRIMARY KEY,
                sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
                bot_id uuid NOT NULL REFERENCES bots (id) ON DELETE CASCADE,
                engine_order_id bigint NOT NULL CHECK (engine_order_id > 0),
                kind varchar(16) NOT NULL CHECK (kind IN ('Submit', 'Cancel')),
                client_order_id varchar(64) NOT NULL,
                payload jsonb NULL,
                from_kill boolean NOT NULL,
                status varchar(16) NOT NULL CHECK (status IN ('Pending', 'Sending', 'Sent', 'Failed', 'Rejected')),
                attempts integer NOT NULL CHECK (attempts BETWEEN 0 AND 10),
                next_attempt_at timestamp with time zone NULL,
                claimed_at timestamp with time zone NULL,
                place_started_at timestamp with time zone NULL,
                sent_at timestamp with time zone NULL,
                rejection_reason varchar(32) NULL,
                rejection_message varchar(500) NULL,
                created_at timestamp with time zone NOT NULL,
                UNIQUE (bot_id, engine_order_id, kind),
                CHECK ((kind = 'Submit') = (payload IS NOT NULL)),
                CHECK ((status = 'Rejected') = (rejection_reason IS NOT NULL))
            );
            CREATE INDEX bot_order_intents_due ON bot_order_intents (status, next_attempt_at, sequence)
                WHERE status IN ('Pending', 'Sending');
            CREATE INDEX bot_order_intents_bot ON bot_order_intents (bot_id, sequence);

            CREATE TABLE trading_halt (
                id boolean PRIMARY KEY DEFAULT true CHECK (id),
                halted boolean NOT NULL,
                updated_at timestamp with time zone NOT NULL
            );
            INSERT INTO trading_halt (id, halted, updated_at) VALUES (true, false, now());
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            DROP TABLE trading_halt;
            DROP TABLE bot_order_intents;
            DROP TABLE bot_checkpoints;
            DROP TABLE bot_leases;
            DROP TABLE bots;
            """);
    }
}
