using Microsoft.AspNetCore.Builder;

namespace MatriX.API.Middlewares
{
    public static class Extensions
    {
        public static IApplicationBuilder UseModHeaders(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ModHeaders>();
        }

        public static IApplicationBuilder UseAccs(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<Accs>();
        }

        public static IApplicationBuilder UseIPTables(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<IPTables>();
        }

        public static IApplicationBuilder UseTorAPI(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<TorAPI>();
        }

        public static IApplicationBuilder UseRemoteAPI(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<RemoteAPI>();
        }
    }
}
