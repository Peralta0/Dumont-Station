// SPDX-FileCopyrightText: 2026 Project Dumont
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Robust.Shared.GameStates;

namespace Content.Goobstation.Shared.SurveillanceCamera;

/// <summary>
/// Adds opt-in support for systems that need to use normal surveillance cameras
/// as restricted remote console viewpoints.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ConsoleCameraComponent : Component
{
    /// <summary>
    /// Console networks allowed to use this camera.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<string> Tags = [];

    /// <summary>
    /// Maximum unobstructed viewing distance from the camera, in meters.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Range = 7.5f;
}
