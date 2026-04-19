// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using g0v0.Server.Common.Database.MySQL;
using g0v0.Server.Common.Database.PostgreSQL;
using g0v0.Server.Common.Extensions;
using Microsoft.EntityFrameworkCore;

namespace g0v0.Server.Realtime;

/// <summary>
/// Application entry point for the g0v0 realtime API server.
/// </summary>
public static class Program
{
    /// <summary>
    /// Configures and runs the web host.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddSingleton<IConfigPathProvider, WebConfigPathProvider>();

        var generalConfigManager = new ConfigurationManager<GeneralConfiguration>(builder.Environment.ContentRootPath);
        var generalConfig = generalConfigManager.Value;
        builder.Services.AddSingleton(generalConfigManager);

        builder.Services.AddRepositories(generalConfig.UseLegacyDatabase);
        if (generalConfig.UseLegacyDatabase)
        {
            builder.Services.AddDbContext<MysqlDbContext>(options =>
                options.UseMySql(
                    generalConfig.MySqlConnectionString,
                    ServerVersion.AutoDetect(generalConfig.MySqlConnectionString)));
        }
        else
        {
            builder.Services.AddDbContext<PostgreSqlDbContext>(options =>
                options.UseNpgsql(generalConfig.PostgresqlConnectionString));
        }

        builder.Services.AddOAuthAuthentication();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}