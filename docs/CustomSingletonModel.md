# CustomSingletonModel

### Is this the right Class to use?
This Class is applied to Everything in the game as opposed to any AbstractModel extend which is applied only when that is relevant.

Such as CustomCardModel or CustomRelicModel, so when you wanna apply logic to those its preferable to use them instead.

If you want to create a custom "Game Mode" of sorts, maybe consider using ModifierModel which allows you to add a modifier to a Custom Run.

## Usage
This is basically an AbstractModel that is applied to the game unconditionally, so any Function/Hook in that class should work with this.

This class has 2 mandatory fields:
- registerSettings
	- An Enum with settings/flags on when to hook into.
- modId
	- A string of your modId (just use `MainFile.ModId`)

So as an example if you want to fire something at the start of every run you could do this:

```csharp
class AtRunStart : CustomSingletonModel {
    public override SingletonSettings registerSettings => new SingletonSettings() { SubscribeToRunStateHooks = true };
    public override string modId => MainFile.ModId;
    public override Task AfterActEntered(IRunState runState) {
        if (runState.CurrentActIndex != 0) return;
        MainFile.Logger.Info("We started a Run");
        return base.AfterActEntered();
    }
}
```

## Using a Singleton without BaseLib
CustomSingletonModel is essentially just a wrapper around `ModHelper.SubscribeForRunStateHooks`/`ModHelper.SubscribeForCombatStateHooks` which you can Subscribe to yourself.

You would need to provide a method that collects a list of your Singelton classes and provides them to `ModHelper`:
```csharp
public class Subscriber
{
	public static readonly MyCustomModel CustomModel = ModelDb.Get<MyCustomModel>();
		
	public static void subscribe()
	{
		ModHelper.SubscribeForRunStateHooks(MainFile.ModId, CollectCustomModels);
	}
	
	public static IEnumerable<AbstractModel> CollectCustomModels(RunState runState)
	{
		List<AbstractModel> results = [CustomModel];
		return results;
	}
}
```
Your Model than also having the `public override bool ShouldReceiveCombatHooks => true;` if you want it to also get Combat Hooks.
```csharp
public class MyCustomModel : AbstractModel
{
    public override bool ShouldReceiveCombatHooks => true;

    public override Task AfterActEntered() {
        MainFile.Logger.Info("We entered an Act");
        return Task.CompletedTask;
    }
}
```

And than calling `Subscriber.subscribe();` in your MainFile's `Initialize()` function.

The `CustomSingletonModel` class just seeks to reduce the boilerplate of this and fetch and subscribe all `CustomSingletonModel` automatically.