using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Cms.Controllers;

/// <summary>
///     Returns the seeded dictionary items as a flat, grouped list.
///     Requires an authenticated Umbraco member (the wrapper forwards the member cookie).
/// </summary>
[ApiController]
[Route("api/wrapper/translations")]
[Produces("application/json")]
public sealed class WrapperTranslationsController : ControllerBase
{
    private const string SeedMarkerKey = "Wrapper.Seeded";

    private readonly IMemberManager _memberManager;
    private readonly IDictionaryItemService _dictionaryItemService;
    private readonly ILanguageService _languageService;
    private readonly ILogger<WrapperTranslationsController> _logger;

    public WrapperTranslationsController(
        IMemberManager memberManager,
        IDictionaryItemService dictionaryItemService,
        ILanguageService languageService,
        ILogger<WrapperTranslationsController> logger)
    {
        _memberManager = memberManager;
        _dictionaryItemService = dictionaryItemService;
        _languageService = languageService;
        _logger = logger;
    }

    public sealed record TranslationDto(string Key, string Path, string? Group, Dictionary<string, string> Values);

    /// <summary>Unauthenticated readiness probe: true once the startup seeder has run.</summary>
    [HttpGet("~/api/wrapper/health")]
    public async Task<IActionResult> Health()
    {
        var seeded = await _dictionaryItemService.ExistsAsync(SeedMarkerKey);
        var languages = (await _languageService.GetAllAsync()).Select(l => l.IsoCode).OrderBy(c => c).ToArray();
        var rootCount = (await _dictionaryItemService.GetAtRootAsync()).Count(r => r.ItemKey != SeedMarkerKey);
        return Ok(new { seeded, languages, rootGroups = rootCount });
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        MemberIdentityUser? member = await _memberManager.GetCurrentMemberAsync();
        if (member is null)
        {
            return Unauthorized(new { error = "not authenticated" });
        }

        string[] languages = (await _languageService.GetAllAsync())
            .Select(l => l.IsoCode)
            .OrderBy(c => c)
            .ToArray();

        var items = new List<TranslationDto>();
        foreach (IDictionaryItem root in (await _dictionaryItemService.GetAtRootAsync()).OrderBy(r => r.ItemKey))
        {
            if (string.Equals(root.ItemKey, SeedMarkerKey, StringComparison.Ordinal))
            {
                continue;
            }

            await FlattenAsync(root, parentPath: null, items);
        }

        _logger.LogInformation(
            "wrapper-api: translations served to {Username} ({Count} items, {Languages})",
            member.UserName,
            items.Count,
            string.Join(",", languages));

        return Ok(new { languages, count = items.Count, items });
    }

    private async Task FlattenAsync(IDictionaryItem item, string? parentPath, List<TranslationDto> accumulator)
    {
        string path = parentPath is null ? item.ItemKey : $"{parentPath} / {item.ItemKey}";

        accumulator.Add(new TranslationDto(
            item.ItemKey,
            path,
            parentPath,
            item.Translations
                .Where(t => !string.IsNullOrEmpty(t.Value))
                .ToDictionary(t => t.LanguageIsoCode, t => t.Value)));

        foreach (IDictionaryItem child in (await _dictionaryItemService.GetChildrenAsync(item.Key)).OrderBy(c => c.ItemKey))
        {
            await FlattenAsync(child, path, accumulator);
        }
    }
}
