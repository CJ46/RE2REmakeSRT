﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace RE2REmakeSRT
{
    public class JSONServerStartup
    {
        JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions()
        {
            WriteIndented = true
        };

        public JSONServerStartup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {

        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseDeveloperExceptionPage();

            app.Run(async context =>
            {
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync<GameMemory>(context.Response.Body, Program.gameMemory, jsonSerializerOptions);
            });
        }
    }
}
