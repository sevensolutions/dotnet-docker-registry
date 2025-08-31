using DotNetDockerRegistry.Options;
using DotNetDockerRegistry.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace DotNetDockerRegistry.Services
{
    public static class DockerIdentityAuthenticationDefaults
    {
        public const string AuthenticationScheme = "DockerIdentity";
    }

    public sealed class DockerIdentityAuthenticationOptions : AuthenticationSchemeOptions
    {
    }

    public sealed class DockerIdentityAuthenticationHandler : AuthenticationHandler<DockerIdentityAuthenticationOptions>
    {
        private readonly IDockerIdentityProvider _identityProvider;

        public DockerIdentityAuthenticationHandler(IOptionsMonitor<DockerIdentityAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock, IDockerIdentityProvider patProvider)
            : base(options, logger, encoder, clock)
        {
            _identityProvider = patProvider;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (string.IsNullOrEmpty(Request.Headers.Authorization))
                return AuthenticateResult.Fail($"Not authorized.");

            string headerValue = Request.Headers.Authorization.First().Substring("Bearer ".Length);

            var principal = await _identityProvider.GetPrincipalAsync(headerValue);

            if (principal is null)
                return AuthenticateResult.Fail("Invalid token.");

            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
    }

    public interface IDockerIdentityProvider
    {
        ValueTask<string> CreateTokenAsync(string role, Claim[]? claims = null, DateTime? expirationDate = null);
        ValueTask<ClaimsPrincipal?> GetPrincipalAsync(string token);
    }

    public sealed class DockerIdentityProvider : IDockerIdentityProvider
    {
        public const string Audience = "Docker";

        private readonly string _serverUrl;
        private readonly SymmetricSecurityKey _securityKey;

        public DockerIdentityProvider(IOptions<DockerRegistryOptions> options)
        {
            _serverUrl = options.Value.ServerUrl;
            _securityKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(options.Value.Secret));
        }

        public ValueTask<string> CreateTokenAsync(string role, Claim[]? claims = null, DateTime? expirationDate = null)
        {
            var credentials = new SigningCredentials(_securityKey, SecurityAlgorithms.HmacSha256);

            var allClaims = new List<Claim>
        {
            new Claim( "role", role )
        };

            if (claims is not null)
                allClaims.AddRange(claims);

            var token = new JwtSecurityToken(
                issuer: _serverUrl,
                audience: Audience,
                claims: allClaims,
                expires: expirationDate,
                signingCredentials: credentials
            );

            return ValueTask.FromResult(new JwtSecurityTokenHandler().WriteToken(token));
        }

        public ValueTask<ClaimsPrincipal?> GetPrincipalAsync(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            var validationParameters = new TokenValidationParameters()
            {
                ValidIssuer = _serverUrl,
                ValidateIssuer = true,
                ValidateLifetime = true,
                RequireExpirationTime = false,
                ValidAudience = Audience,
                ValidateAudience = true,
                IssuerSigningKey = _securityKey
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            var identity = new ClaimsIdentity(principal.Claims, DockerIdentityAuthenticationDefaults.AuthenticationScheme);

            var claimsPrincipal = new ClaimsPrincipal(identity);

            return ValueTask.FromResult<ClaimsPrincipal?>(claimsPrincipal);
        }
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    public static class DockerIdentityExtensions
    {
        public static AuthenticationBuilder AddDockerIdentity(this AuthenticationBuilder builder)
            => builder.AddScheme<DockerIdentityAuthenticationOptions, DockerIdentityAuthenticationHandler>(DockerIdentityAuthenticationDefaults.AuthenticationScheme, _ => { });
        public static AuthenticationBuilder AddDockerIdentity(this AuthenticationBuilder builder, Action<DockerIdentityAuthenticationOptions>? options)
            => builder.AddScheme<DockerIdentityAuthenticationOptions, DockerIdentityAuthenticationHandler>(DockerIdentityAuthenticationDefaults.AuthenticationScheme, options);
    }
}