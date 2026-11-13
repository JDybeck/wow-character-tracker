using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

// Known recipes (Recipes.lua): read from the open profession window under its category headers, kept
// per character and profession, and shared with friends (FriendData.lua) under Professions.
public class RecipesLuaTests
{
    const string Me = "Player-1-0000000A";

    // A profession window: whose list it shows, and its recipes in order ({ id, category, learned }).
    const string TradeSkill = """
        window = { name = 'Alchemy', linked = false, list = {} }
        professions = { 'Alchemy', 'Herbalism', nil, nil, 'Cooking' }
        categories = {}
        local function categoryId(name)
          for i, n in ipairs(categories) do if n == name then return i end end
          categories[#categories + 1] = name
          return #categories
        end
        local function find(id)
          for _, r in ipairs(window.list) do if r[1] == id then return r end end
        end
        C_TradeSkillUI = {
          GetBaseProfessionInfo = function() return { professionName = window.name } end,
          IsTradeSkillLinked = function() return window.linked end,
          GetAllRecipeIDs = function()
            local ids = {}
            for _, r in ipairs(window.list) do ids[#ids + 1] = r[1] end
            return ids
          end,
          GetRecipeInfo = function(id)
            local r = find(id) or { id, 'Potions', true }
            return { recipeID = id, learned = r[3] ~= false, categoryID = categoryId(r[2]) }
          end,
          GetCategoryInfo = function(id) return { categoryID = id, name = categories[id] } end,
          GetProfessionInfoByRecipeID = function(id) return { professionName = 'Alchemy' } end,
        }
        function GetProfessions() return 1, 2, nil, nil, 5 end
        function GetProfessionInfo(i) return professions[i] end
        """;

    static FriendsLuaTests.Client Client(string db = "{ chars = { ['Player-1-0000000A'] = { char = 'Zeb' } }, items = {} }")
    {
        var c = new FriendsLuaTests.Client(Me, "{}", db, files: ["Shared.lua", "FriendData.lua", "Recipes.lua"]);
        c.Lua(TradeSkill);
        return c;
    }

    static string Str(FriendsLuaTests.Client c, string expr) => c.Lua("return " + expr).String;

    [Fact]
    public void Learned_recipes_are_kept_under_their_categories_in_the_windows_order()
    {
        var c = Client();
        c.Lua("""
            window.list = { { 3170, 'Elixirs' }, { 9999, 'Elixirs', false }, { 2330, 'Potions' }, { 2329, 'Elixirs' } }
            ns.scanRecipes()
            """);
        Assert.Equal("Elixirs=3170,2329/Potions=2330", Str(c, $"WoWCharacterTrackerDB.recipes['{Me}'].Alchemy"));
        Assert.Equal("Alchemy~Elixirs=3170,2329/Potions=2330", Str(c, $"WoWCharacterTrackerDB.chars['{Me}'].recipes"));

        // Another profession joins the text in name order; one the character dropped leaves it.
        c.Lua("window = { name = 'Cooking', list = { { 2538, 'Meat' } } }; ns.scanRecipes()");
        Assert.Equal("Alchemy~Elixirs=3170,2329/Potions=2330\nCooking~Meat=2538", Str(c, "ns.ownRecipeText()"));
        c.Lua("professions[5] = 'Fishing'");
        Assert.Equal("Alchemy~Elixirs=3170,2329/Potions=2330", Str(c, "ns.ownRecipeText()"));
    }

    // Someone else's list (a link, the guild's) is never taken as ours, and a scan only ever adds.
    [Fact]
    public void A_linked_window_is_skipped_and_a_smaller_scan_keeps_what_was_known()
    {
        var c = Client();
        c.Lua("window.linked = true; window.list = { { 2329, 'Elixirs' } }; ns.scanRecipes()");
        Assert.True(c.Lua($"return WoWCharacterTrackerDB.recipes['{Me}'].Alchemy").IsNil());

        c.Lua("window.linked = false; window.list = { { 2329, 'Elixirs' }, { 3170, 'Elixirs' } }; ns.scanRecipes()");
        c.Lua("window.list = { { 7183, 'Elixirs' }, { 2330, 'Potions' } }; ns.scanRecipes()"); // a filtered list
        Assert.Equal("Elixirs=7183,2329,3170/Potions=2330", Str(c, $"WoWCharacterTrackerDB.recipes['{Me}'].Alchemy"));
    }

    // A recipe learned later goes at the end of its category, but only into a list already read from the window.
    [Fact]
    public void A_new_recipe_joins_a_known_list_only()
    {
        var c = Client();
        c.Lua("ns.addLearnedRecipe(2330)");
        Assert.True(c.Lua($"return WoWCharacterTrackerDB.recipes['{Me}'].Alchemy").IsNil());

        c.Lua("window.list = { { 2330, 'Potions' }, { 2329, 'Elixirs' } }; ns.scanRecipes(); ns.addLearnedRecipe(2331)");
        Assert.Equal("Alchemy~Potions=2330,2331/Elixirs=2329", Str(c, $"WoWCharacterTrackerDB.chars['{Me}'].recipes"));
    }

    // Friends get the recipes with Professions on, cut at whole ids, and the app reads them back.
    [Fact]
    public void Recipes_go_to_friends_with_professions_and_come_back_whole()
    {
        var c = Client();
        c.Lua("""
            local ids = {}
            for i = 1, 700 do ids[#ids + 1] = 10000 + i end
            char = { char = 'Zeb', lastSeen = 5, professions = 'Alchemy 300/300',
              recipes = 'Alchemy~Elixirs=2329,3170/Potions=2330\nTailoring~Cloth=' .. table.concat(ids, ',') .. '/Bags=99\nZinc~Z=1' }
            with = ns.decodeFriendChar(ns.encodeFriendChar('Player-1-0000000B', char, nil, { professions = true }))
            without = ns.decodeFriendChar(ns.encodeFriendChar('Player-1-0000000B', char, nil, {}))
            """);
        Assert.True(c.Lua("return without.recipes").IsNil());
        var text = Str(c, "with.recipes");
        Assert.True(text.Length <= 3000, $"{text.Length}");
        Assert.Matches(@"^Alchemy~Elixirs=2329,3170/Potions=2330\nTailoring~Cloth=10001(,\d{5})+$", text); // never half an id, nothing after it's full
        Assert.Equal("Potions", Str(c, "ns.friendRecipes(with)[1].groups[2].name"));

        c.Lua("friends = { ['Zeb#2222'] = { chars = { ['Player-1-0000000B'] = with } } }");
        var snapshot = Assert.Single(Assert.Single(FriendData.ReadFriends(c.Lua("return friends"))).Characters);
        var recipes = ProfessionSkills.Recipes(snapshot.Recipes);
        Assert.Equal(["Elixirs", "Potions"], recipes["Alchemy"].Select(g => g.Category));
        Assert.Equal([2329, 3170], recipes["Alchemy"][0].Ids);
        Assert.Equal(10001, recipes["Tailoring"][0].Ids[0]);
    }

    // An addon from before recipes drops a character over 8 KB, so recipes give way rather than everything.
    [Fact]
    public void Recipes_are_left_out_when_the_text_would_be_too_long_for_older_addons()
    {
        var c = Client();
        c.Lua("""
            local events, levels = {}, {}
            for i = 1, 10 do events[i] = { kind = 'loot', at = 1000 + i, level = 60, item = string.rep('i', 90), itemId = 19019, quality = 4 } end
            for i = 2, 81 do levels[#levels + 1] = { level = i, seconds = 1234567, at = 1793746800 } end
            char = { char = 'Zeb', lastSeen = 5, talents = string.rep('t', 1024), recipes = 'Alchemy~Potions=' .. string.rep('12345,', 600) .. '1' }
            items = { gear = string.rep('1~2,', 128), enchants = string.rep('e', 2048) }
            allowed = { professions = true, gear = true, talents = true, highlights = true, leveling = true }
            text = ns.encodeFriendChar('Player-1-0000000B', char, items, allowed, events, levels)
            """);
        var text = Str(c, "text");
        Assert.True(text.Length <= 8192, $"{text.Length}");
        Assert.DoesNotContain("recipes=", text);
        Assert.Contains("talents=", text);
        c.Lua("char.recipes = 'Alchemy~Potions=2329'; small = ns.encodeFriendChar('Player-1-0000000B', char, nil, { professions = true })");
        Assert.Contains("recipes=Alchemy~Potions=2329", Str(c, "small"));
    }

    [Fact]
    public void Recipes_merge_into_one_list_per_category_without_repeats()
    {
        var c = Client();
        Assert.Equal("A=3,1/B=4,2", Str(c, "ns.mergeRecipes('A=1/B=2', { { name = 'A', ids = { 3, 1 } }, { name = 'B', ids = { 4, 0, 1.5 } } })"));
        Assert.Equal("=1,2", Str(c, "ns.mergeRecipes('1,2', {})")); // the first, uncategorized format
        Assert.Equal("Bad=1", Str(c, "ns.mergeRecipes('', { { name = 'B/a=d', ids = { 1 } } })"));
        Assert.Equal("", Str(c, "ns.recipeText({}, nil)"));
    }
}
