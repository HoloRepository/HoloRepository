using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoloRepository.Core.Segmentation;

// DTOs mirror the HoloRepository server's API contract (src/server/API contract.md, v0.1.0).

public enum ApiFileType
{
    Dicom,
    Nifti,
    NiftiSegmented,
    Mz3,
}

public enum ApiJobState
{
    InQueue,
    InProgress,
    Done,
    Error,
}

public sealed record ApiUser(string Id, string Username);

public sealed record ApiToken(string AccessToken, string TokenType = "bearer");

public sealed record ApiJob(string Id);

public sealed record ApiJobStatus(ApiJobState Status, string? Msg = null);

public sealed record ApiFileRecord(string Id, ApiFileType FileType);

public sealed record ApiRecord(string Id, IReadOnlyList<ApiFileRecord> Files);

public sealed class HoloRepositoryApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>
/// Typed client for the HoloRepository server (FastAPI). Workflow: sign in, upload a zipped DICOM folder
/// or a NIfTI file with a model, poll the job, then download the record's files.
/// </summary>
public sealed class HoloRepositoryApiClient : IDisposable
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public HoloRepositoryApiClient(Uri baseAddress, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = baseAddress;
        _http.Timeout = TimeSpan.FromMinutes(10);
        _ownsClient = true;
    }

    public HoloRepositoryApiClient(HttpClient http)
    {
        _http = http;
    }

    public Uri? BaseAddress => _http.BaseAddress;

    public string? AccessToken { get; set; }

    public bool IsSignedIn => !string.IsNullOrEmpty(AccessToken);

    public async Task<ApiUser> SignUpAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = username, ["password"] = password });
        using var response = await _http.PostAsync("signup", content, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<ApiUser>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiUser> SignInAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password,
        });
        using var response = await _http.PostAsync("token", content, cancellationToken).ConfigureAwait(false);
        var token = await ReadAsync<ApiToken>(response, cancellationToken).ConfigureAwait(false);
        AccessToken = token.AccessToken;
        return await GetMeAsync(cancellationToken).ConfigureAwait(false);
    }

    public void SignOut() => AccessToken = null;

    public async Task<ApiUser> GetMeAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "me", null, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<ApiUser>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiJob> CreateJobAsync(Stream file, string fileName, ApiFileType fileType, string model,
        CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(file);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(fileType == ApiFileType.Dicom ? "application/zip" : "application/octet-stream");
        // Quote field names (name="file") as in RFC 7578; .NET leaves them unquoted by default.
        content.Add(fileContent, "\"file\"", $"\"{fileName}\"");
        content.Add(new StringContent(ToWire(fileType)), "\"file_type\"");
        content.Add(new StringContent(model), "\"model\"");
        using var response = await SendAsync(HttpMethod.Post, "new-job", content, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<ApiJob>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiJobStatus> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"job-status?job_id={Uri.EscapeDataString(jobId)}", null, cancellationToken)
            .ConfigureAwait(false);
        return await ReadAsync<ApiJobStatus>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ApiRecord>> GetRecordsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "records", null, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<List<ApiRecord>>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> DownloadFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"files/{Uri.EscapeDataString(fileId)}", null, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public static string ToWire(ApiFileType type) => JsonSerializer.Serialize(type, Json).Trim('"');

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        if (!IsSignedIn) throw new HoloRepositoryApiException(HttpStatusCode.Unauthorized, "Sign in to the HoloRepository server first.");
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false);
        return value ?? throw new HoloRepositoryApiException(response.StatusCode, "The server returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HoloRepositoryApiException(response.StatusCode, DescribeError(response.StatusCode, body));
    }

    /// <summary>Turns FastAPI error bodies ({"detail": "..."} or validation lists) into a readable sentence.</summary>
    internal static string DescribeError(HttpStatusCode status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
            {
                if (detail.ValueKind == JsonValueKind.String) return detail.GetString()!;
                if (detail.ValueKind == JsonValueKind.Array)
                {
                    var messages = detail.EnumerateArray()
                        .Select(e => e.TryGetProperty("msg", out var m) ? m.GetString() : null)
                        .Where(m => !string.IsNullOrEmpty(m));
                    string joined = string.Join("; ", messages);
                    if (joined.Length > 0) return joined;
                }
            }
        }
        catch (JsonException)
        {
        }

        return status switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
            HttpStatusCode.NotFound => "The server could not find that item.",
            _ => $"The server returned an error ({(int)status}).",
        };
    }
}
