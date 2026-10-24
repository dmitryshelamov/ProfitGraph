using FluentMigrator;

namespace ProfitGraph.Infrastructure.Common.Postgres.Migrations;

[Migration(202610250005)]
public sealed class AddOptimizationTrialStageAndNeighborhoodMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            ALTER TABLE optimization_trials
                ADD COLUMN stage smallint NOT NULL DEFAULT 1 CHECK (stage IN (1, 2)),
                ADD COLUMN neighbors_evaluated integer NULL CHECK (neighbors_evaluated >= 0),
                ADD COLUMN neighbors_eligible integer NULL CHECK (neighbors_eligible >= 0),
                ADD COLUMN neighborhood_mean numeric NULL,
                ADD CONSTRAINT optimization_trials_neighborhood CHECK ((neighbors_evaluated IS NULL) = (neighbors_eligible IS NULL));
            """);
    }

    public override void Down()
    {
        Execute.Sql(
            """
            ALTER TABLE optimization_trials
                DROP CONSTRAINT optimization_trials_neighborhood,
                DROP COLUMN neighborhood_mean,
                DROP COLUMN neighbors_eligible,
                DROP COLUMN neighbors_evaluated,
                DROP COLUMN stage;
            """);
    }
}
