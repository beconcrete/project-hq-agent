using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HqAgent.Agents.Services;

public class DocumentTextExtractor
{
    private const string PdfExtractionModel = "gpt-4.1-mini";

    private readonly IHttpClientFactory _httpFactory;
    private readonly string _apiKey;
    private readonly ILogger<DocumentTextExtractor> _logger;

    public DocumentTextExtractor(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<DocumentTextExtractor> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _apiKey = config["OPENAI_API_KEY"]
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not configured");
    }

    public async Task<string> ExtractAsync(
        byte[] bytes,
        string contentType,
        string fileName,
        CancellationToken ct)
    {
        if (IsPdf(contentType, fileName))
            return await ExtractPdfTextAsync(bytes, ct);

        if (IsDocx(contentType, fileName))
            return ExtractDocxText(bytes);

        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<string> ExtractPdfTextAsync(byte[] pdfBytes, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = PdfExtractionModel,
            max_tokens = 8192,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "file",
                            file = new
                            {
                                filename = "document.pdf",
                                file_data = $"data:application/pdf;base64,{Convert.ToBase64String(pdfBytes)}",
                            }
                        },
                        new { type = "text", text = "Extract all text content from this document verbatim, preserving structure. No commentary." }
                    }
                }
            }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Add("Authorization", $"Bearer {_apiKey}");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var http = _httpFactory.CreateClient();
        using var resp = await http.SendAsync(req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("OpenAI PDF extraction error {Status}: {Body}", resp.StatusCode, err);
            throw new HttpRequestException($"OpenAI returned {(int)resp.StatusCode}: {err}");
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        var text = JsonDocument.Parse(json).RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        _logger.LogInformation("PDF text extracted: {CharCount} chars", text.Length);
        return text;
    }

    private static string ExtractDocxText(byte[] docxBytes)
    {
        using var ms = new MemoryStream(docxBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("DOCX document.xml was not found");

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var paragraphs = doc.Descendants(w + "p")
            .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)))
            .Select(s => s.Trim())
            .Where(s => s.Length > 0);

        return string.Join(Environment.NewLine, paragraphs);
    }

    private static bool IsPdf(string contentType, string fileName) =>
        contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsDocx(string contentType, string fileName) =>
        contentType.Contains("wordprocessingml.document", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
}
