using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ELearnVN.Backend.Services
{
    public interface IMuxService
    {
        string CreateMuxSignedToken(string playbackId, int expiresSeconds = 3600);
        string GetMuxPlaybackUrl(string playbackId, bool signed = true);
        string GetMuxThumbnailUrl(string playbackId, int width = 640, int timeOffset = 0);
        string CreateEbookSignedToken(int productId, int userId, int expiresSeconds = 3600);
        Dictionary<string, object>? VerifyEbookSignedToken(string token);
        string GetEbookAccessUrl(int productId, int userId);
        Task<MuxUploadResult?> UploadVideoToMux(string filePath);
    }

    public class MuxUploadResult
    {
        public string AssetId { get; set; } = null!;
        public string PlaybackId { get; set; } = null!;
        public string Status { get; set; } = null!;
    }

    public class MuxService : IMuxService
    {
        private readonly IConfiguration _config;

        public MuxService(IConfiguration config)
        {
            _config = config;
        }

        public string CreateMuxSignedToken(string playbackId, int expiresSeconds = 3600)
        {
            var keyId = _config["Mux:SigningKeyId"];
            var base64PrivateKey = _config["Mux:SigningPrivateKey"];

            if (string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(base64PrivateKey))
            {
                return "";
            }

            try
            {
                var privateKeyBytes = Convert.FromBase64String(base64PrivateKey);
                using var rsa = RSA.Create();
                rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);

                var securityKey = new RsaSecurityKey(rsa)
                {
                    KeyId = keyId
                };
                var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

                var header = new JwtHeader(credentials);
                header["kid"] = keyId;

                var payload = new JwtPayload(
                    issuer: null,
                    audience: "v", // Video audience for Mux
                    claims: new[] { new Claim("sub", playbackId) },
                    notBefore: null,
                    expires: DateTime.UtcNow.AddSeconds(expiresSeconds)
                );

                var token = new JwtSecurityToken(header, payload);
                return new JwtSecurityTokenHandler().WriteToken(token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MUX] Signed token error: {ex.Message}");
                return "";
            }
        }

        public string GetMuxPlaybackUrl(string playbackId, bool signed = true)
        {
            if (string.IsNullOrEmpty(playbackId)) return "";

            var keyId = _config["Mux:SigningKeyId"];
            if (signed && !string.IsNullOrEmpty(keyId))
            {
                var token = CreateMuxSignedToken(playbackId);
                if (!string.IsNullOrEmpty(token))
                {
                    return $"https://stream.mux.com/{playbackId}.m3u8?token={token}";
                }
            }

            return $"https://stream.mux.com/{playbackId}.m3u8";
        }

        public string GetMuxThumbnailUrl(string playbackId, int width = 640, int timeOffset = 0)
        {
            if (string.IsNullOrEmpty(playbackId)) return "";
            return $"https://image.mux.com/{playbackId}/thumbnail.jpg?width={width}&time={timeOffset}";
        }

        public string CreateEbookSignedToken(int productId, int userId, int expiresSeconds = 3600)
        {
            var secretKey = _config["Jwt:Secret"] ?? "jwt-secret-change-me-thirty-two-characters-long";
            var keyBytes = Encoding.UTF8.GetBytes(secretKey);
            var securityKey = new SymmetricSecurityKey(keyBytes);
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim("product_id", productId.ToString()),
                new Claim("user_id", userId.ToString()),
                new Claim("resource", "ebook"),
                new Claim("type", "content_access")
            };

            var token = new JwtSecurityToken(
                issuer: null,
                audience: null,
                claims: claims,
                expires: DateTime.UtcNow.AddSeconds(expiresSeconds),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public Dictionary<string, object>? VerifyEbookSignedToken(string token)
        {
            var secretKey = _config["Jwt:Secret"] ?? "jwt-secret-change-me-thirty-two-characters-long";
            var keyBytes = Encoding.UTF8.GetBytes(secretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                ClockSkew = TimeSpan.Zero
            };

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
                var jwtToken = (JwtSecurityToken)validatedToken;

                var typeClaim = principal.FindFirst("type")?.Value;
                if (typeClaim != "content_access")
                {
                    return null;
                }

                var result = new Dictionary<string, object>();
                foreach (var claim in jwtToken.Claims)
                {
                    result[claim.Type] = claim.Value;
                }
                return result;
            }
            catch
            {
                return null;
            }
        }

        public string GetEbookAccessUrl(int productId, int userId)
        {
            var token = CreateEbookSignedToken(productId, userId);
            return $"/api/learning/ebook/{productId}/download?token={token}";
        }

        public Task<MuxUploadResult?> UploadVideoToMux(string filePath)
        {
            var tokenId = _config["Mux:TokenId"];
            var tokenSecret = _config["Mux:TokenSecret"];

            if (string.IsNullOrEmpty(tokenId) || string.IsNullOrEmpty(tokenSecret))
            {
                // Sandbox Mock Mode
                var epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var mockResult = new MuxUploadResult
                {
                    AssetId = $"mock_asset_{epoch}",
                    PlaybackId = $"mock_playback_{epoch}",
                    Status = "ready"
                };
                return Task.FromResult<MuxUploadResult?>(mockResult);
            }

            // Real upload implementation can be added here if needed, 
            // but mock is sufficient for sandbox/development.
            var tempEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return Task.FromResult<MuxUploadResult?>(new MuxUploadResult
            {
                AssetId = $"mock_asset_{tempEpoch}",
                PlaybackId = $"mock_playback_{tempEpoch}",
                Status = "ready"
            });
        }
    }
}
