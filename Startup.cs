using MatriX.API.Middlewares;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace MatriX.API
{
    public class Startup
    {
        public static IHttpClientFactory httpClientFactory = default;

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient("ts").ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler()
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.None,
                    AllowAutoRedirect = true
                };

                handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                return handler;
            });

            services.AddHttpClient("base").ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler()
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip,
                    AllowAutoRedirect = true
                };

                handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                return handler;
            });

            services.AddControllersWithViews().AddJsonOptions(options => {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
            });

            services.AddResponseCompression(options =>
            {
                options.Providers.Clear();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = ["application/json", "application/json; charset=utf-8"];
                options.EnableForHttps = true;
            });

            services.Configure<GzipCompressionProviderOptions>(o =>
            {
                o.Level = CompressionLevel.SmallestSize;
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHttpClientFactory _httpClientFactory)
        {
            app.UseDeveloperExceptionPage();
            httpClientFactory = _httpClientFactory;

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

            app.UseRouting();
            app.UseResponseCompression();
            
            app.UseRequestLogging();
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
