namespace Ustin.Work.LessMess.UmbarcoWrapper.Cms.Composing;

/// <summary>A dictionary item to seed. Folders leave <see cref="En"/>/<see cref="De"/> null.</summary>
public sealed record SeedNode(string Key, string? En, string? De, params SeedNode[] Children)
{
    public SeedNode(string key, string? en, string? de) : this(key, en, de, Array.Empty<SeedNode>())
    {
    }
}

/// <summary>
///     ~45 dictionary items: 8 folders (2 of them nested one level deeper) and 37 leaf
///     translations, each with an en-US and a de-DE value.
/// </summary>
public static class SeedData
{
    private static SeedNode Leaf(string key, string en, string de) => new(key, en, de);

    private static SeedNode Folder(string key, params SeedNode[] children) => new(key, null, null, children);

    public static readonly SeedNode[] Tree =
    {
        Folder("General",
            Leaf("General.Save", "Save", "Speichern"),
            Leaf("General.Cancel", "Cancel", "Abbrechen"),
            Leaf("General.Delete", "Delete", "Löschen"),
            Leaf("General.Edit", "Edit", "Bearbeiten"),
            Leaf("General.Search", "Search", "Suchen"),
            Leaf("General.Welcome", "Welcome", "Willkommen"),
            Folder("General.Buttons",
                Leaf("General.Buttons.Submit", "Submit", "Absenden"),
                Leaf("General.Buttons.Reset", "Reset", "Zurücksetzen"),
                Leaf("General.Buttons.Next", "Next", "Weiter"),
                Leaf("General.Buttons.Back", "Back", "Zurück"))),

        Folder("Navigation",
            Leaf("Navigation.Home", "Home", "Startseite"),
            Leaf("Navigation.Products", "Products", "Produkte"),
            Leaf("Navigation.About", "About us", "Über uns"),
            Leaf("Navigation.Contact", "Contact", "Kontakt"),
            Leaf("Navigation.Account", "My account", "Mein Konto")),

        Folder("Errors",
            Leaf("Errors.NotFound", "Page not found", "Seite nicht gefunden"),
            Leaf("Errors.ServerError", "Something went wrong", "Etwas ist schief gelaufen"),
            Leaf("Errors.Unauthorized", "You are not signed in", "Sie sind nicht angemeldet"),
            Leaf("Errors.Forbidden", "Access denied", "Zugriff verweigert"),
            Folder("Errors.Validation",
                Leaf("Errors.Validation.Required", "This field is required", "Dieses Feld ist erforderlich"),
                Leaf("Errors.Validation.Email", "Enter a valid email address", "Geben Sie eine gültige E-Mail-Adresse ein"),
                Leaf("Errors.Validation.MinLength", "Value is too short", "Der Wert ist zu kurz"),
                Leaf("Errors.Validation.PasswordMismatch", "Passwords do not match", "Passwörter stimmen nicht überein"))),

        Folder("Emails",
            Leaf("Emails.WelcomeSubject", "Welcome to LessMess", "Willkommen bei LessMess"),
            Leaf("Emails.WelcomeBody", "Thanks for signing up!", "Danke für Ihre Anmeldung!"),
            Leaf("Emails.ResetSubject", "Reset your password", "Setzen Sie Ihr Passwort zurück"),
            Leaf("Emails.Footer", "You receive this email because you have an account", "Sie erhalten diese E-Mail, weil Sie ein Konto haben")),

        Folder("Account",
            Leaf("Account.SignIn", "Sign in", "Anmelden"),
            Leaf("Account.SignOut", "Sign out", "Abmelden"),
            Leaf("Account.Register", "Create account", "Konto erstellen"),
            Leaf("Account.Username", "Username", "Benutzername"),
            Leaf("Account.Password", "Password", "Passwort"),
            Leaf("Account.Email", "Email", "E-Mail"),
            Leaf("Account.RememberMe", "Remember me", "Angemeldet bleiben")),

        Folder("Dashboard",
            Leaf("Dashboard.Title", "Translations", "Übersetzungen"),
            Leaf("Dashboard.Empty", "No translations found", "Keine Übersetzungen gefunden"),
            Leaf("Dashboard.Count", "translations loaded", "Übersetzungen geladen")),
    };
}
