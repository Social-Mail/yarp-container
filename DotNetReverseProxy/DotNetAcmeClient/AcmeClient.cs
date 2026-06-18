using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using RetroCoreFit;
using System.Linq;

namespace DotNetAcmeClient;

public partial class AcmeClient
{
    private readonly HttpClient _httpClient;
    private readonly string _directoryUrl;
    private readonly string _externalKid;
    private readonly string _externalHmacKey;
    private RSA _accountKey;
    private string _accountUrl;
    private AcmeDirectory _directory;
    private JsonWebKey? jwk;

    private System.Text.Json.JsonSerializerOptions jsonOptions = new JsonSerializerOptions {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    public AcmeClient(HttpClient httpClient, string directoryUrl, string accountKeyPath)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _directoryUrl = directoryUrl ?? throw new ArgumentNullException(nameof(directoryUrl));

        // Set up default headers for ACME requests
        SetupDefaultHeaders();

        // Load existing account key or create new one
        LoadOrCreateAccountKey(accountKeyPath ?? throw new ArgumentNullException(nameof(accountKeyPath)));
    }

    public AcmeClient(HttpClient httpClient, string directoryUrl, string externalAccountBindingKID, string externalAccountBindingHmacKey)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _directoryUrl = directoryUrl ?? throw new ArgumentNullException(nameof(directoryUrl));
        _externalKid = externalAccountBindingKID ?? throw new ArgumentNullException(nameof(externalAccountBindingKID));
        _externalHmacKey= externalAccountBindingHmacKey ?? throw new ArgumentNullException(nameof(externalAccountBindingHmacKey));
    }

    private void SetupDefaultHeaders()
    {
        // Set default Accept header for ACME
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Set User-Agent
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ACME-Client/1.0");
    }

    private void LoadOrCreateAccountKey(string _accountKeyPath)
    {
        if (File.Exists(_accountKeyPath))
        {
            // Load existing account key
            try
            {
                var pemContent = File.ReadAllText(_accountKeyPath);
                _accountKey = RSA.Create();
                var keyBytes = Convert.FromBase64String(pemContent);
                _accountKey.ImportRSAPrivateKey(keyBytes, out _);
                return;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load account key from {_accountKeyPath}: {ex.Message}", ex);
            }
        }
        else
        {
            // Create new account key
            _accountKey = RSA.Create(2048);
            var privateKeyBytes = _accountKey.ExportRSAPrivateKey();
            var pemContent = Convert.ToBase64String(privateKeyBytes);
            File.WriteAllText(_accountKeyPath, pemContent);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var request = RequestBuilder.Get(_directoryUrl);
        _directory = (await request.GetResponseAsync<AcmeDirectory>(_httpClient, cancellationToken))!;
        Console.WriteLine($"Found: {_directory.NewAccount}");
        await EnsureAccountExistsAsync(cancellationToken);
    }

    private async Task EnsureAccountExistsAsync(CancellationToken cancellationToken = default)
    {

        var payload = JsonSerializer.Serialize(new
        {
            termsOfServiceAgreed = true,
            contact = new[] { "mailto:admin@example.com" }
        });

        var ar = await this.ApiRequest(_directory.NewAccount, payload, cancellationToken, false, true);
        var ac = (await ar.GetResponseAsync<AcmeAccount>(_httpClient, cancellationToken))!;

        this._accountUrl = ac.Location;

    }


    public async Task<AcmeOrder> CreateOrderAsync(IEnumerable<string> domains, CancellationToken cancellationToken = default)
    {

        var identifiers = new List<object>();
        foreach (var domain in domains)
        {
            identifiers.Add(new { type = "dns", value = domain });
        }

        var request = await ApiRequest(_directory.NewOrder, new { identifiers }, cancellationToken, true, false);

        var order = (await request.GetResponseAsync<AcmeOrder>(_httpClient, cancellationToken))!;
        return order;
    }

    public async Task<AcmeAuthorization> GetAuthorizationAsync(string authorizationUrl, CancellationToken cancellationToken = default)
    {
        var request = await ApiRequest(authorizationUrl, new {  }, cancellationToken, true, false);

        return (await request.GetResponseAsync<AcmeAuthorization>(_httpClient, cancellationToken))!;
    }

    public async Task<AcmeChallenge> GetChallengeAsync(string challengeUrl, CancellationToken cancellationToken = default)
    {
        var request = await ApiRequest(challengeUrl, new {  }, cancellationToken, true, false);

        return (await request.GetResponseAsync<AcmeChallenge>(_httpClient, cancellationToken))!;
    }

    public async Task CompleteChallengeAsync(string challengeUrl, string keyAuthorization, CancellationToken cancellationToken = default)
    {
        var request = await ApiRequest(challengeUrl, new { keyAuthorization }, cancellationToken, true, false);
        await request.GetResponseAsync(_httpClient, cancellationToken);
    }

    public async Task<AcmeOrder> FinalizeOrderAsync(string orderUrl, string csr, CancellationToken cancellationToken = default)
    {
        var request = await ApiRequest(orderUrl, new { csr }, cancellationToken, true, false);
        return (await request.GetResponseAsync<AcmeOrder>(_httpClient, cancellationToken))!;
    }

    public async Task<string> DownloadCertificateAsync(string certificateUrl, CancellationToken cancellationToken = default)
    {
        var request = await ApiRequest(certificateUrl, new {  }, cancellationToken, true, false);
        var response = (await request.GetResponseAsync<AcmeCertificate>(_httpClient, cancellationToken))!;
        return response.Certificate;
    }

    public RSA GetAccountKey()
    {
        return _accountKey;
    }

    public string GetAccountKeyPem()
    {
        var privateKeyBytes = _accountKey.ExportRSAPrivateKey();
        return Convert.ToBase64String(privateKeyBytes);
    }


    // Helper method to create JWK for signing
    private JsonWebKey GetJwk()
    {
        if (this.jwk != null)
            return jwk;

        var rsaParams = _accountKey.ExportParameters(false);
        jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsaParams));
        jwk.D = null;
        jwk.P = null;
        jwk.Q = null;
        jwk.DP = null;
        jwk.DQ = null;
        jwk.QI = null;
        return jwk;
    }
}
