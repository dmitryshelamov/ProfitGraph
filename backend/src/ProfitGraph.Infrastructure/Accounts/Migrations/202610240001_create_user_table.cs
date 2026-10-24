using FluentMigrator;

namespace ProfitGraph.Infrastructure.Accounts.Migrations;

[Migration(202610240001)]
public sealed class CreateUserTableMigration : Migration
{
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE users (
                id uuid PRIMARY KEY,
                login varchar(100) NOT NULL CHECK (length(trim(login)) > 0),
                password_hash text NOT NULL,
                role varchar(16) NOT NULL CHECK (role IN ('Owner', 'User')),
                status varchar(16) NOT NULL CHECK (status IN ('Active', 'Blocked')),
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX users_login_lower_unique ON users (lower(login));
            """);
    }

    public override void Down()
    {
        Execute.Sql("DROP TABLE users;");
    }
}