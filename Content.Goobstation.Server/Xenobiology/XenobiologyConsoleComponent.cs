// SPDX-FileCopyrightText: 2026 Project Dumont
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.Actions.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Server.Xenobiology;

[RegisterComponent]
public sealed partial class XenobiologyConsoleComponent : Component
{
    public const string SlimeContainerId = "xenobiology-console-slimes";

    /// <summary>
    /// Remote viewpoint spawned while a user controls the console.
    /// </summary>
    [DataField]
    public EntProtoId RemoteEntityPrototype = "XenobiologyConsoleEye";

    /// <summary>
    /// Mob created when spending monkey biomass.
    /// </summary>
    [DataField]
    public EntProtoId MonkeyPrototype = "MobMonkey";

    /// <summary>
    /// Items with this tag can be inserted to add biomass.
    /// </summary>
    [DataField]
    public ProtoId<TagPrototype> MonkeyCubeTag = "MonkeyCube";

    /// <summary>
    /// Maximum distance from the remote viewpoint for monkey interactions, in meters.
    /// </summary>
    [DataField]
    public float InteractionRange = 1.5f;

    /// <summary>
    /// Maximum distance from the remote viewpoint for slime interactions, in meters.
    /// </summary>
    [DataField]
    public float SlimeTargetRange = 1.25f;

    /// <summary>
    /// Maximum distance the operator can remain from the console, in meters.
    /// </summary>
    [DataField]
    public float UserMaxDistance = 2f;

    /// <summary>
    /// Interval between checks for changes that do not move the remote, such as camera power loss.
    /// </summary>
    [DataField]
    public TimeSpan SessionValidationInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// Minimum interval between mouse shortcut requests from the operator.
    /// </summary>
    [DataField]
    public TimeSpan ShortcutCooldown = TimeSpan.FromSeconds(0.1);

    /// <summary>
    /// Maximum distance to search for an available camera, in meters.
    /// </summary>
    [DataField]
    public float CameraSearchRange = 128f;

    /// <summary>
    /// Camera network tag required for viewing and remote interactions.
    /// </summary>
    [DataField]
    public string RequiredCameraTag = "Xenobiology";

    /// <summary>
    /// Extra distance around the viewport to search for cameras contributing visible tiles.
    /// </summary>
    [DataField]
    public float CameraOverlaySearchRange = 16f;

    /// <summary>
    /// Biomass added by inserting one monkey cube.
    /// </summary>
    [DataField]
    public FixedPoint2 MonkeyCubeBiomass = 1;

    /// <summary>
    /// Biomass spent to create one monkey.
    /// </summary>
    [DataField]
    public FixedPoint2 MonkeySpawnCost = 1;

    /// <summary>
    /// Biomass recovered from one dead monkey.
    /// </summary>
    [DataField]
    public FixedPoint2 MonkeyRecycleYield = 0.2;

    /// <summary>
    /// Sound played when collecting a slime or recycling a monkey.
    /// </summary>
    [DataField]
    public SoundSpecifier SuctionSound = new SoundPathSpecifier("/Audio/_Goobstation/Xenobiology/air_suck.ogg");

    /// <summary>
    /// Sound played when releasing stored slimes or creating a monkey.
    /// </summary>
    [DataField]
    public SoundSpecifier ReleaseSound = new SoundPathSpecifier("/Audio/_Goobstation/Xenobiology/air_shoot.ogg");

    /// <summary>
    /// Maximum number of living or dead slimes the console can hold.
    /// </summary>
    [DataField]
    public int MaxStoredSlimes = 5;

    /// <summary>
    /// Console output port used to send dead slimes to a linked grinder.
    /// </summary>
    [DataField]
    public ProtoId<SourcePortPrototype> GrinderOutputPort = "XenobiologySlimeTransfer";

    /// <summary>
    /// Grinder input port accepted by the console transfer link.
    /// </summary>
    [DataField]
    public ProtoId<SinkPortPrototype> GrinderInputPort = "XenobiologySlimeReceiver";

    [ViewVariables]
    public FixedPoint2 MonkeyBiomass;

    [ViewVariables]
    public Container SlimeContainer = default!;

    [ViewVariables]
    public EntityUid? ActiveUser;

    [ViewVariables]
    public EntityUid? RemoteEntity;
}

[RegisterComponent]
public sealed partial class XenobiologyConsoleControllerComponent : Component
{
    /// <summary>
    /// Action used to leave the remote console session.
    /// </summary>
    [DataField]
    public EntProtoId<ActionComponent> ExitAction = "ActionXenobiologyConsoleExit";

    /// <summary>
    /// Action used to create a monkey at the remote viewpoint.
    /// </summary>
    [DataField]
    public EntProtoId<ActionComponent> PlaceMonkeyAction = "ActionXenobiologyConsolePlaceMonkey";

    /// <summary>
    /// Action used to recycle a nearby dead monkey into biomass.
    /// </summary>
    [DataField]
    public EntProtoId<ActionComponent> RecycleMonkeyAction = "ActionXenobiologyConsoleRecycleMonkey";

    /// <summary>
    /// Action used to store a nearby slime or send its corpse to a linked grinder.
    /// </summary>
    [DataField]
    public EntProtoId<ActionComponent> GrabSlimeAction = "ActionXenobiologyConsoleGrabSlime";

    /// <summary>
    /// Action used to release all stored slimes at the remote viewpoint.
    /// </summary>
    [DataField]
    public EntProtoId<ActionComponent> ReleaseSlimesAction = "ActionXenobiologyConsoleReleaseSlimes";

    /// <summary>
    /// Action used to send a nearby slime scan to the operator.
    /// </summary>
    [DataField]
    public EntProtoId<ActionComponent> AnalyzeSlimeAction = "ActionXenobiologyConsoleAnalyzeSlime";

    /// <summary>
    /// Action used to show the console mouse shortcuts.
    /// </summary>
    [DataField]
    public EntProtoId<ActionComponent> ShowShortcutsAction = "ActionXenobiologyConsoleShowShortcuts";

    [ViewVariables]
    public EntityUid? Console;

    [ViewVariables]
    public EntityUid? RemoteEntity;

    [ViewVariables]
    public EntityUid? ExitActionEntity;

    [ViewVariables]
    public EntityUid? PlaceMonkeyActionEntity;

    [ViewVariables]
    public EntityUid? RecycleMonkeyActionEntity;

    [ViewVariables]
    public EntityUid? GrabSlimeActionEntity;

    [ViewVariables]
    public EntityUid? ReleaseSlimesActionEntity;

    [ViewVariables]
    public EntityUid? AnalyzeSlimeActionEntity;

    [ViewVariables]
    public EntityUid? ShowShortcutsActionEntity;

    [ViewVariables]
    public TimeSpan NextValidationTime;

    [ViewVariables]
    public TimeSpan NextShortcutTime;

    [ViewVariables]
    public EntityUid? ActiveCamera;

    [ViewVariables]
    public EntityCoordinates? LastValidCoordinates;

    [ViewVariables]
    public EntityUid? PreviousEyeTarget;

    [ViewVariables]
    public EntityUid? PreviousRelayEntity;

    [ViewVariables]
    public bool? PreviousDrawFov;

    [ViewVariables]
    public bool? PreviousDrawLight;

    [ViewVariables]
    public bool CleaningUp;
}
