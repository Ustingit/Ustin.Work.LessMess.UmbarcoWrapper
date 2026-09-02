using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Cms.Composing;

/// <summary>
///     Seeds two languages and a small tree of grouped dictionary items (translations)
///     the first time the CMS boots, so the wrapper has something to list.
///     Idempotent: guarded by a marker dictionary item.
/// </summary>
public sealed class TranslationSeeder : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string MarkerKey = "Wrapper.Seeded";
    private static readonly Guid UserKey = Constants.Security.SuperUserKey;

    private readonly ILanguageService _languageService;
    private readonly IDictionaryItemService _dictionaryItemService;
    private readonly ILogger<TranslationSeeder> _logger;

    public TranslationSeeder(
        ILanguageService languageService,
        IDictionaryItemService dictionaryItemService,
        ILogger<TranslationSeeder> logger)
    {
        _languageService = languageService;
        _dictionaryItemService = dictionaryItemService;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (await _dictionaryItemService.ExistsAsync(MarkerKey))
        {
            return;
        }

        ILanguage english = await EnsureLanguageAsync("en-US", "English (United States)");
        ILanguage german = await EnsureLanguageAsync("de-DE", "German (Germany)");

        var created = 0;
        foreach (SeedNode root in SeedData.Tree)
        {
            created += await CreateNodeAsync(root, parentId: null, english, german);
        }

        var marker = new DictionaryItem(MarkerKey)
        {
            Translations = new[] { new DictionaryTranslation(english, DateTime.UtcNow.ToString("o")) },
        };
        await _dictionaryItemService.CreateAsync(marker, UserKey);

        _logger.LogInformation("TranslationSeeder: created {Count} dictionary items across {Groups} groups", created, SeedData.Tree.Length);
    }

    private async Task<ILanguage> EnsureLanguageAsync(string isoCode, string name)
    {
        ILanguage? existing = await _languageService.GetAsync(isoCode);
        if (existing is not null)
        {
            return existing;
        }

        Attempt<ILanguage, Umbraco.Cms.Core.Services.OperationStatus.LanguageOperationStatus> result =
            await _languageService.CreateAsync(new Language(isoCode, name), UserKey);

        if (!result.Success)
        {
            _logger.LogWarning("TranslationSeeder: could not create language {Iso}: {Status}", isoCode, result.Status);
        }

        return result.Result;
    }

    private async Task<int> CreateNodeAsync(SeedNode node, Guid? parentId, ILanguage english, ILanguage german)
    {
        var translations = new List<IDictionaryTranslation>();
        if (node.En is not null)
        {
            translations.Add(new DictionaryTranslation(english, node.En));
        }

        if (node.De is not null)
        {
            translations.Add(new DictionaryTranslation(german, node.De));
        }

        var item = new DictionaryItem(parentId, node.Key) { Translations = translations };
        Attempt<IDictionaryItem, Umbraco.Cms.Core.Services.OperationStatus.DictionaryItemOperationStatus> result =
            await _dictionaryItemService.CreateAsync(item, UserKey);

        if (!result.Success)
        {
            _logger.LogWarning("TranslationSeeder: could not create dictionary item {Key}: {Status}", node.Key, result.Status);
            return 0;
        }

        var count = 1;
        foreach (SeedNode child in node.Children)
        {
            count += await CreateNodeAsync(child, result.Result.Key, english, german);
        }

        return count;
    }
}
