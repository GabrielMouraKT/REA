using REA.Core;
using REA.Domain;

namespace REA.UnitTests;

public class PageRequestTests
{
    [Theory]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(200, 200)]
    [InlineData(500, 500)]
    [InlineData(10, 50)]
    [InlineData(1000, 50)]
    [InlineData(0, 50)]
    public void Only_allowed_page_sizes_are_accepted(int requested, int expected) =>
        Assert.Equal(expected, new PageRequest(1, requested).Size);

    [Fact]
    public void Page_below_one_becomes_one_and_skip_is_computed()
    {
        Assert.Equal(1, new PageRequest(-3).Page);
        Assert.Equal(100, new PageRequest(3, 50).Skip);
    }

    [Fact]
    public void Total_pages_is_at_least_one()
    {
        Assert.Equal(1, new PagedResult<int>([], 0, new PageRequest()).TotalPages);
        Assert.Equal(3, new PagedResult<int>([], 120, new PageRequest(1, 50)).TotalPages);
    }
}

public class ProjectValidationTests
{
    private readonly ProjectInputValidator _validator = new();

    [Theory]
    [InlineData("", "Informe o nome do projeto.")]
    [InlineData("   ", "Informe o nome do projeto.")]
    [InlineData("ab", "O nome do projeto precisa ter pelo menos 3 caracteres.")]
    public void Invalid_names_have_portuguese_messages(string name, string message)
    {
        var result = _validator.Validate(new ProjectInput(name, null, null));
        Assert.False(result.IsValid);
        Assert.Equal(message, result.Errors[0].ErrorMessage);
    }

    [Fact]
    public void Name_longer_than_limit_is_rejected() =>
        Assert.False(_validator.Validate(new ProjectInput(new string('x', 121), null, null)).IsValid);

    [Fact]
    public void Valid_input_passes() =>
        Assert.True(_validator.Validate(new ProjectInput("MIR4 Lab", "MIR4", "Pesquisa local")).IsValid);

    [Fact]
    public void Normalization_ignores_case_and_extra_spaces() =>
        Assert.Equal(ResearchProject.Normalize("  mir4   lab "), ResearchProject.Normalize("MIR4 LAB"));
}

public class EnvironmentValidationTests
{
    [Fact]
    public void Unknown_kind_is_rejected()
    {
        var result = new EnvironmentInputValidator().Validate(
            new EnvironmentInput("Lab", (EnvironmentKind)99, null, null, null, null));
        Assert.False(result.IsValid);
    }
}

public class NavigationCatalogTests
{
    [Fact]
    public void Keys_are_unique() =>
        Assert.Equal(NavigationCatalog.AllItems.Count(), NavigationCatalog.AllItems.Select(i => i.Key).Distinct().Count());

    [Fact]
    public void All_sections_from_the_spec_are_present()
    {
        var titles = NavigationCatalog.Sections.Select(s => s.Title).ToList();
        Assert.Equal(
            ["DASHBOARD", "PROJETOS", "RESEARCH", "COMPARATIVE ANALYSIS", "LAUNCHER STUDIO", "PORTAL STUDIO",
             "WIKI & KNOWLEDGE", "GM & SERVER ADMIN", "EVIDENCE", "SETTINGS"],
            titles);
    }

    [Fact]
    public void Implemented_items_are_exactly_the_phase_one_pages()
    {
        var implemented = NavigationCatalog.AllItems.Where(i => i.Implemented).Select(i => i.Key).Order().ToList();
        Assert.Equal(
            new[]
            {
                NavigationCatalog.Dashboard, NavigationCatalog.ActivityLog, NavigationCatalog.ProjectsEnvironments,
                NavigationCatalog.ProjectsList, NavigationCatalog.ProjectsNew, NavigationCatalog.SettingsBackup,
                NavigationCatalog.SettingsDatabase, NavigationCatalog.SettingsTools,
            }.Order(),
            implemented);
    }

    [Fact]
    public void Every_item_has_a_phase_and_description() =>
        Assert.All(NavigationCatalog.AllItems, i =>
        {
            Assert.InRange(i.Phase, 1, 9);
            Assert.False(string.IsNullOrWhiteSpace(i.Description));
        });
}
