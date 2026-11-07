using Microsoft.Extensions.Options;

namespace DebugProbe.AspNetCore.Options;

internal sealed class DebugProbeOptionsValidator : IValidateOptions<DebugProbeOptions>
{
    public ValidateOptionsResult Validate(string? name, DebugProbeOptions options)
    {
        var errors = new List<string>();
        
        if (options.MaxRequests < 1)
            errors.Add("MaxRequests must be at least 1.");
        
        if (options.MaxBodySizeKb < 0 || options.MaxBodySizeKb > (int.MaxValue - 1) / 1024)
            errors.Add($"MaxBodySizeKb must be between 0 and {(int.MaxValue - 1) / 1024}.");
        
        if (options.SlowRequestThreshold < TimeSpan.Zero)
            errors.Add("SlowRequestThreshold must be zero or greater.");
        
        if (string.IsNullOrWhiteSpace(options.RoutePrefix) || options.RoutePrefix == "/" ||
            options.RoutePrefix.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_')) ||
            options.RoutePrefix.Contains("//"))
            errors.Add("RoutePrefix must contain a non-empty path using letters, digits, '/', '-' or '_'.");
        
        if (options.ExcludedPaths is null || options.ExcludedPaths.Any(p => string.IsNullOrWhiteSpace(p) || !p.StartsWith('/')))
            errors.Add("ExcludedPaths must contain paths starting with '/'.");
        
        if (options.RedactedHeaders is null || options.RedactedQueryParameters is null || options.RedactedJsonFields is null)
            errors.Add("Redaction lists must not be null. Use an empty array instead.");
       
        if (options.Enabled && options.Server is { } server)
        {
            if (!Uri.TryCreate(server.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                errors.Add("Server.Url must be an absolute HTTP(S) base URL without credentials, query or fragment.");
            
            if (string.IsNullOrWhiteSpace(server.ApiKey) || server.ApiKey.Any(c => c < '!' || c > '~'))
                errors.Add("Server.ApiKey must be a non-empty ingestion key without whitespace.");
            
            if (string.IsNullOrWhiteSpace(server.ApplicationId) || server.ApplicationId.Length > 200)
                errors.Add("Server.ApplicationId must contain 1 to 200 characters.");
            
            if (server.ApplicationName is not null && string.IsNullOrWhiteSpace(server.ApplicationName))
                errors.Add("Server.ApplicationName must not be blank.");
            
            if (server.Environment?.Length > 100)
                errors.Add("Server.Environment must not exceed 100 characters.");
            
            if (server.QueueCapacity is < 1 or > 10000)
                errors.Add("Server.QueueCapacity must be between 1 and 10000.");
            
            if (server.Timeout <= TimeSpan.Zero || server.Timeout > TimeSpan.FromMinutes(1))
                errors.Add("Server.Timeout must be greater than zero and at most one minute.");
           
            if (server.HeartbeatInterval < TimeSpan.FromSeconds(1) || server.HeartbeatInterval > TimeSpan.FromDays(1))
                errors.Add("Server.HeartbeatInterval must be between one second and one day.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
