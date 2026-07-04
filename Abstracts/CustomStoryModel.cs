using BaseLib.Extensions;
using BaseLib.Patches;
using BaseLib.Patches.Content;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Timeline;

namespace BaseLib.Abstracts;



/// <summary>
/// Despite the name these are not stored in <see cref="ModelDb"/>
/// </summary>
public abstract class CustomStoryModel : StoryModel, ICustomModel
{

    // TODO: Prefixing Mod Id.
    // Id gets slugified in a place where we can't easily add the prefix after like with ModelDb.GetEntry
    // Id not uppercased/Slugified because game will Slugify it later!
    /// <summary>
    /// Must match 1 to 1 with <see cref="CustomEpochModel.StoryId"/> where used. <br/>
    /// This currently does not automatically add the Mod Prefix!
    /// </summary>
    protected override string Id => $"{GetType().GetPrefix()}{GetType().Name}";
    
    /// <summary>
    /// Should only be called once from <seealso cref="PostModInitPatch"/>
    /// </summary>
    internal static void FillStoryDictionaries(List<CustomStoryModel> models)
    {
        BaseLibMain.Logger.Info("Inserting CustomStories into dictionaries");
        var storyTypeDictionary = (AccessTools.Field(typeof(StoryModel), "_storyTypeDictionary").GetValue(null) as Dictionary<string, Type>)!;
        foreach (var customStoryModel in models)
        {
            var type = customStoryModel.GetType();
            BaseLibMain.Logger.Debug($"CustomStory Type: {type.Name} | Id: {customStoryModel.Id} | Saved in dict as: {StringHelper.Slugify(customStoryModel.Id)}");
            CustomContentDictionary.AddStory(customStoryModel);
            // slugify it to match what the game does for lookup even if it currently removes the prefix '-'
            storyTypeDictionary[StringHelper.Slugify(customStoryModel.Id)] = type;
        }
    }
}