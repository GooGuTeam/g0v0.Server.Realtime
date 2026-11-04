// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Common.Configuration;
using g0v0.Server.Realtime.Notifications.Bot.Commands;
using Newtonsoft.Json;
using NUnit.Framework;

namespace g0v0.Server.Realtime.Tests.Notifications;

/// <summary>
/// Tests for the <c>!faq</c> argument parsing, lookup and fallback rules.
/// </summary>
[TestFixture]
public class FaqCatalogTests
{
    private string _basePath = null!;

    [SetUp]
    public void SetUp()
    {
        _basePath = Path.Combine(Path.GetTempPath(), "g0v0-faq-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_basePath, "config"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }

    [Test]
    public void BuildReply_WithoutArguments_ShouldReturnUsage()
    {
        Assert.That(FaqCatalog.BuildReply(CreateConfig(), []), Is.EqualTo(FaqCatalog.UsageReply));
    }

    [Test]
    public void BuildReply_WhenConfigurationIsMissing_ShouldStillReturnUsage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FaqCatalog.BuildReply(config: null, []), Is.EqualTo(FaqCatalog.UsageReply));
            Assert.That(FaqCatalog.BuildReply(config: null, ["list"]), Is.EqualTo(FaqCatalog.EmptyReply));
            Assert.That(FaqCatalog.BuildReply(config: null, ["anything"]), Is.EqualTo(FaqCatalog.NotFoundReply));
        });
    }

    [Test]
    public void BuildReply_List_ShouldReportUsableEntriesInConfigurationOrder()
    {
        Assert.That(
            FaqCatalog.BuildReply(CreateConfig(), ["list"]),
            Is.EqualTo("Available entries: list-item-a, how-can-i-help, zh-only, mixed-case, multi-line, long-answer."));
    }

    [Test]
    public void BuildReply_List_ShouldIgnoreCaseAndTrailingArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["LIST"]), Does.StartWith("Available entries: "));
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["list", "extra"]), Does.StartWith("Available entries: "));
        });
    }

    [Test]
    public void BuildReply_List_ShouldIgnoreLanguagePrefix()
    {
        Assert.That(
            FaqCatalog.BuildReply(CreateConfig(), ["en:list"]),
            Is.EqualTo(FaqCatalog.BuildReply(CreateConfig(), ["list"])));
    }

    [Test]
    public void BuildReply_List_WithoutEntries_ShouldReportEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FaqCatalog.BuildReply(new FaqConfig(), ["list"]), Is.EqualTo(FaqCatalog.EmptyReply));
            Assert.That(FaqCatalog.BuildReply(new FaqConfig { Entries = null! }, ["list"]), Is.EqualTo(FaqCatalog.EmptyReply));
        });
    }

    [Test]
    public void BuildReply_ShouldReturnTheDefaultLanguageText()
    {
        Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["how-can-i-help"]), Is.EqualTo("Help en"));
    }

    [Test]
    public void BuildReply_ShouldMatchTheEntryIdCaseInsensitively()
    {
        Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["HOW-CAN-I-HELP"]), Is.EqualTo("Help en"));
    }

    [Test]
    public void BuildReply_ShouldReturnTheRequestedLanguage()
    {
        Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["zh:list-item-a"]), Is.EqualTo("A zh"));
    }

    [Test]
    public void BuildReply_ShouldMatchLanguageByPrimarySubtag()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["ZH-cn:list-item-a"]), Is.EqualTo("A zh"));
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["zh-Hans:zh-only"]), Is.EqualTo("Z zh"));
        });
    }

    [Test]
    public void BuildReply_WhenLanguageIsUnknown_ShouldFallBackToDefaultWithoutMarker()
    {
        Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["de:list-item-a"]), Is.EqualTo("A en"));
    }

    [Test]
    public void BuildReply_WhenDefaultLanguageIsMissing_ShouldReturnAnyTranslation()
    {
        Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["en:zh-only"]), Is.EqualTo("Z zh"));
    }

    [Test]
    public void BuildReply_ShouldMatchTranslationKeysCaseInsensitively()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["mixed-case"]), Is.EqualTo("Mixed en"));
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["zh:mixed-case"]), Is.EqualTo("Mixed zh"));
        });
    }

    [Test]
    public void BuildReply_WithEmptyLanguagePrefix_ShouldUseTheDefaultLanguage()
    {
        Assert.That(FaqCatalog.BuildReply(CreateConfig(), [":list-item-a"]), Is.EqualTo("A en"));
    }

    [Test]
    public void BuildReply_WithEmptyEntryAfterPrefix_ShouldReturnUsage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["en:"]), Is.EqualTo(FaqCatalog.UsageReply));
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), [":"]), Is.EqualTo(FaqCatalog.UsageReply));
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), [":   "]), Is.EqualTo(FaqCatalog.UsageReply));
        });
    }

    [Test]
    public void BuildReply_WhenPrefixIsNotALanguageTag_ShouldTreatTheWholeArgumentAsId()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["nope"]), Is.EqualTo(FaqCatalog.NotFoundReply));
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["abc:list-item-a"]), Is.EqualTo(FaqCatalog.NotFoundReply));
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["中文:list-item-a"]), Is.EqualTo(FaqCatalog.NotFoundReply));
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["list-item-a:"]), Is.EqualTo(FaqCatalog.NotFoundReply));
        });
    }

    [Test]
    public void BuildReply_ShouldSkipEntriesThatCannotAnswer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["blank-values"]), Is.EqualTo(FaqCatalog.NotFoundReply));
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["no-translations"]), Is.EqualTo(FaqCatalog.NotFoundReply));
            Assert.That(FaqCatalog.BuildReply(CreateConfig(), [string.Empty]), Is.EqualTo(FaqCatalog.UsageReply));
        });
    }

    [Test]
    public void BuildReply_ShouldKeepTheFirstOfDuplicateIds()
    {
        Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["how-can-i-help"]), Is.EqualTo("Help en"));
    }

    [Test]
    public void BuildReply_ShouldKeepLineBreaksInTheAnswer()
    {
        Assert.That(FaqCatalog.BuildReply(CreateConfig(), ["multi-line"]), Is.EqualTo("line1\nline2"));
    }

    [Test]
    public void ConfigManager_ShouldLoadEntriesFromJson()
    {
        const string json = """
            {
              "DefaultLanguage": "zh",
              "Entries": [
                {
                  "Id": "how-can-i-help",
                  "Translations": { "en": "Help en", "zh": "Help zh" }
                }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(_basePath, "config", "faq.json"), json);

        FaqConfig config = new ConfigManager(_basePath).Get<FaqConfig>();

        Assert.Multiple(() =>
        {
            Assert.That(config.DefaultLanguage, Is.EqualTo("zh"));
            Assert.That(config.Entries, Has.Count.EqualTo(1));
            Assert.That(config.Entries[0].Id, Is.EqualTo("how-can-i-help"));
            Assert.That(config.Entries[0].Translations["zh"], Is.EqualTo("Help zh"));
            Assert.That(FaqCatalog.BuildReply(config, ["how-can-i-help"]), Is.EqualTo("Help zh"));
        });
    }

    [Test]
    public void ConfigManager_WhenTheFileIsMissing_ShouldReturnNull()
    {
        Assert.That(new ConfigManager(_basePath).TryGet<FaqConfig>(), Is.Null);
    }

    [Test]
    public void ConfigManager_WithExplicitNullEntries_ShouldNotThrow()
    {
        File.WriteAllText(Path.Combine(_basePath, "config", "faq.json"), """{"Entries": null}""");

        FaqConfig? config = new ConfigManager(_basePath).TryGet<FaqConfig>();

        Assert.That(FaqCatalog.BuildReply(config, ["list"]), Is.EqualTo(FaqCatalog.EmptyReply));
    }

    [Test]
    public void ConfigManager_WithMalformedJson_ShouldThrow()
    {
        File.WriteAllText(Path.Combine(_basePath, "config", "faq.json"), """{"Entries": [}""");

        Assert.That(
            () => new ConfigManager(_basePath).Get<FaqConfig>(),
            Throws.InstanceOf<JsonException>());
    }

    private static FaqConfig CreateConfig()
    {
        return new FaqConfig
        {
            DefaultLanguage = "en",
            Entries =
            [
                new FaqEntry
                {
                    Id = "list-item-a",
                    Translations = new(StringComparer.Ordinal) { ["en"] = "A en", ["zh"] = "A zh" },
                },
                new FaqEntry
                {
                    Id = "how-can-i-help",
                    Translations = new(StringComparer.Ordinal) { ["en"] = "Help en" },
                },
                new FaqEntry
                {
                    Id = "how-can-i-help",
                    Translations = new(StringComparer.Ordinal) { ["en"] = "Duplicate en" },
                },
                new FaqEntry
                {
                    Id = "zh-only",
                    Translations = new(StringComparer.Ordinal) { ["zh"] = "Z zh" },
                },
                new FaqEntry
                {
                    Id = "mixed-case",
                    Translations = new(StringComparer.Ordinal) { ["EN"] = "Mixed en", ["ZH"] = "Mixed zh" },
                },
                new FaqEntry
                {
                    Id = "multi-line",
                    Translations = new(StringComparer.Ordinal) { ["en"] = "line1\nline2" },
                },
                new FaqEntry
                {
                    Id = "long-answer",
                    Translations = new(StringComparer.Ordinal)
                    {
                        ["en"] = string.Join(' ', Enumerable.Range(0, 40).Select(static index => $"tip {index}")),
                    },
                },
                new FaqEntry
                {
                    Id = "blank-values",
                    Translations = new(StringComparer.Ordinal) { ["en"] = "   " },
                },
                new FaqEntry { Id = "no-translations", Translations = [] },
                new FaqEntry
                {
                    Id = "   ",
                    Translations = new(StringComparer.Ordinal) { ["en"] = "Blank id" },
                },
            ],
        };
    }
}