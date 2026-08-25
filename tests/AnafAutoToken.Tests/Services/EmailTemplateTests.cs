using AnafAutoToken.Core.Services;
using FluentAssertions;

namespace AnafAutoToken.Tests.Services;

/// <summary>
/// Szablony i kod podstawiający wartości muszą pasować do siebie - te testy pilnują, żeby
/// edycja HTML-a nie wyłączyła po cichu któregoś pola w wysyłanej wiadomości.
/// </summary>
public class EmailTemplateTests
{
    private const string SuccessTemplate = "TokenRefreshSuccessTemplate";
    private const string ErrorTemplate = "TokenRefreshErrorTemplate";
    private const string NoRefreshTemplate = "TokenNoRefreshNeededTemplate";

    [Theory]
    [InlineData(SuccessTemplate, 2)]
    [InlineData(ErrorTemplate, 3)]
    [InlineData(NoRefreshTemplate, 3)]
    public void Template_ContainsEveryPlaceholderTheServiceSubstitutes(string templateName, int placeholderCount)
    {
        var template = RepositoryPaths.ReadEmailTemplate(templateName);

        for (var index = 0; index < placeholderCount; index++)
        {
            template.Should().Contain($"{{{index}}}", $"szablon {templateName} musi mieć pole {{{index}}}");
        }
    }

    [Theory]
    [InlineData(SuccessTemplate)]
    [InlineData(ErrorTemplate)]
    [InlineData(NoRefreshTemplate)]
    public void Template_SharesTheCommonLayout(string templateName)
    {
        var template = RepositoryPaths.ReadEmailTemplate(templateName);

        template.Should().StartWith("<!DOCTYPE html>");
        template.Should().Contain("<html lang=\"pl\">");
        template.Should().Contain("width:600px");
        template.Should().Contain("ANAF Auto Token");
        template.Should().Contain("Wiadomość wygenerowana automatycznie");

        // Układ oparty na tabelach - klient pocztowy Outlooka renderuje silnikiem Worda
        // i ignoruje arkusz z <head>, więc style muszą być przy elementach.
        template.Should().NotContain("<style", "style w <head> nie zadziała w Outlooku");
        template.Should().Contain("role=\"presentation\"");
    }

    [Fact]
    public void ErrorTemplate_UsesTheAlertPalette()
    {
        var template = RepositoryPaths.ReadEmailTemplate(ErrorTemplate);

        template.Should().Contain("#c0392b", "nagłówek błędu ma być w kolorystyce alertu");
        template.Should().Contain("Wymagana natychmiastowa interwencja");
    }

    [Fact]
    public void ErrorTemplate_KeepsTheDetailsSectionBetweenItsMarkers()
    {
        var template = RepositoryPaths.ReadEmailTemplate(ErrorTemplate);

        var start = template.IndexOf("<!--SZCZEGOLY_START-->", StringComparison.Ordinal);
        var end = template.IndexOf("<!--SZCZEGOLY_END-->", StringComparison.Ordinal);
        var placeholder = template.IndexOf("{2}", StringComparison.Ordinal);

        start.Should().BeGreaterThan(-1);
        end.Should().BeGreaterThan(start);
        placeholder.Should().BeInRange(start, end, "pole {2} musi leżeć w usuwalnej sekcji");
    }

    [Fact]
    public void ApplyExceptionDetails_WithoutException_RemovesTheWholeSection()
    {
        var template = RepositoryPaths.ReadEmailTemplate(ErrorTemplate);

        var rendered = EmailNotificationService.ApplyExceptionDetails(template, exception: null);

        rendered.Should().NotContain("{2}");
        rendered.Should().NotContain("SZCZEGOLY_START");
        rendered.Should().NotContain("Szczegóły techniczne", "pusta ramka tylko myliłaby odbiorcę");

        // Reszta wiadomości musi zostać nietknięta.
        rendered.Should().Contain("{0}").And.Contain("{1}");
        rendered.Should().Contain("Wymagana natychmiastowa interwencja");
    }

    [Fact]
    public void ApplyExceptionDetails_WithException_FillsAndEncodesTheSection()
    {
        var template = RepositoryPaths.ReadEmailTemplate(ErrorTemplate);
        var exception = new InvalidOperationException(
            "Refresh token <expired> & revoked",
            new HttpRequestException("Połączenie odrzucone"));

        var rendered = EmailNotificationService.ApplyExceptionDetails(template, exception);

        rendered.Should().Contain("Szczegóły techniczne");
        rendered.Should().Contain("System.InvalidOperationException");
        rendered.Should().Contain("Wyjątek wewnętrzny");
        rendered.Should().Contain("Połączenie odrzucone");

        // Znaki HTML z komunikatu nie mogą rozjechać układu wiadomości.
        rendered.Should().Contain("&lt;expired&gt; &amp; revoked");
        rendered.Should().NotContain("<expired>");
    }
}
