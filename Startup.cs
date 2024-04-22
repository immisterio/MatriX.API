using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;
using MatriX.API.Engine.Middlewares;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading;

namespace MatriX.API
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient("base").ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new System.Net.Http.HttpClientHandler()
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Brotli | System.Net.DecompressionMethods.Deflate,
                    AllowAutoRedirect = true
                };

                handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                return handler;
            });

            services.AddControllersWithViews().AddJsonOptions(options => {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseDeveloperExceptionPage();

            #region IP клиента
            var forwarded = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };

            if (AppInit.settings.KnownProxies != null && AppInit.settings.KnownProxies.Count > 0)
            {
                foreach (var k in AppInit.settings.KnownProxies)
                    forwarded.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse(k.ip), k.prefixLength));
            }

            app.UseForwardedHeaders(forwarded);
            #endregion

            ThreadPool.SetMinThreads(4096, 1024);

            app.UseRouting();
            app.UseModHeaders();
            app.UseAccs();
            app.UseIPTables();
            app.UseRemoteAPI();
            app.UseTorAPI();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
