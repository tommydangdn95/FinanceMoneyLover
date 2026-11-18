using Microsoft.Extensions.Configuration;
using MoneyLoverAutoInput.Core.Categorization;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Tests;

public class CategoryTreeTests
{
    private static List<CategoryNode> Sample() => new()
    {
        new CategoryNode { Name = "Food & Beverage", Children = new() { "Café", "Restaurants" } },
        new CategoryNode { Name = "Fitness" },
        new CategoryNode { Name = "Health & Fitness", Children = new() { "Fitness" } },
    };

    [Fact]
    public void Flatten_lists_parent_then_children_as_paths()
    {
        Assert.Equal(
            new[] { "Food & Beverage", "Food & Beverage > Café", "Food & Beverage > Restaurants",
                    "Fitness", "Health & Fitness", "Health & Fitness > Fitness" },
            CategoryTree.Flatten(Sample()));
    }

    [Fact]
    public void Same_name_as_parent_and_child_is_not_a_duplicate_path()
    {
        Assert.Empty(CategoryTree.FindDuplicates(Sample()));
    }

    [Fact]
    public void Same_child_twice_under_one_parent_is_reported()
    {
        var nodes = new List<CategoryNode> { new() { Name = "Beauty", Children = new() { "Cắt Tóc", "Cắt Tóc" } } };

        Assert.Equal(new[] { "Beauty > Cắt Tóc" }, CategoryTree.FindDuplicates(nodes));
    }

    [Fact]
    public void Split_and_leaf_name()
    {
        Assert.Equal(("Health & Fitness", "Fitness"), CategoryTree.Split("Health & Fitness > Fitness"));
        Assert.Equal(("Fitness", (string?)null), CategoryTree.Split("Fitness"));
        Assert.Equal("Fitness", CategoryTree.LeafName("Health & Fitness > Fitness"));
        Assert.Equal("Books", CategoryTree.LeafName("Books"));
    }

    [Fact]
    public void Format_and_parse_round_trip()
    {
        var text = CategoryTree.Format(Sample());
        var back = CategoryTree.Parse(text);

        Assert.Equal(CategoryTree.Flatten(Sample()), CategoryTree.Flatten(back));
        Assert.Contains("  - Café", text);
    }

    [Fact]
    public void Parse_accepts_indent_or_dash_and_skips_blank_lines()
    {
        var text = "Food & Beverage\r\n    Café\r\n- Restaurants\r\n\r\nBooks\n• Truyện\n  ";

        var nodes = CategoryTree.Parse(text);

        Assert.Equal(2, nodes.Count);
        Assert.Equal(new[] { "Café", "Restaurants" }, nodes[0].Children);
        Assert.Equal(new[] { "Truyện" }, nodes[1].Children);
    }

    [Fact]
    public void Indented_line_before_any_parent_becomes_parent()
    {
        var nodes = CategoryTree.Parse("  - Café\nBooks");

        Assert.Equal(new[] { "Café", "Books" }, nodes.Select(n => n.Name));
    }

    [Theory]
    [InlineData("petrol", "Transportation > Petrol")]          // tên nhóm con, không phải đầu chuỗi
    [InlineData("PETROL", "Transportation > Petrol")]
    [InlineData("cafe", "Food & Beverage > Café")]            // không dấu khớp có dấu
    [InlineData("nuoc mia", "Food & Beverage > Nước Mía")]
    [InlineData("đặt xe", "Transportation > Đặt Xe")]
    [InlineData("transport", "Transportation > Petrol")]       // vẫn khớp theo tên nhóm cha
    [InlineData("tp bank", "TP Bank Tín Dụng")]
    [InlineData("", "Bất kỳ")]
    public void Search_matches_anywhere_ignoring_accents_and_case(string query, string item) =>
        Assert.True(TextNormalizer.ContainsNormalized(item, query));

    [Theory]
    [InlineData("petrol", "Transportation > Taxi")]
    [InlineData("cafe", "Food & Beverage > Bánh Mì")]
    public void Search_rejects_non_matching(string query, string item) =>
        Assert.False(TextNormalizer.ContainsNormalized(item, query));

    [Fact]
    public void Search_over_flattened_tree_finds_child_by_its_own_name()
    {
        var nodes = new List<CategoryNode>
        {
            new() { Name = "Transportation", Children = new() { "Đặt Xe", "Petrol", "Taxi" } },
            new() { Name = "Food & Beverage", Children = new() { "Café" } },
        };

        var hits = CategoryTree.Flatten(nodes).Where(p => TextNormalizer.ContainsNormalized(p, "petrol")).ToList();

        Assert.Equal(new[] { "Transportation > Petrol" }, hits);
    }

    [Fact]
    public void Old_flat_config_still_binds_via_type_converter()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KnownCategories:Chi:0"] = "Ăn uống",
                ["KnownCategories:Chi:1"] = "Khác",
                ["KnownCategories:Thu:0:Name"] = "Salary",
                ["KnownCategories:Thu:0:Children:0"] = "Bonus",
            })
            .Build();

        var s = config.Get<AppSettings>()!;

        Assert.Equal(new[] { "Ăn uống", "Khác" }, s.GetKnownCategories(TransactionType.Chi));
        Assert.Equal(new[] { "Salary", "Salary > Bonus" }, s.GetKnownCategories(TransactionType.Thu));
    }
}
