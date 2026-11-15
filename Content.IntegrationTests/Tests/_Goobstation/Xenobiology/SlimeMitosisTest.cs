// SPDX-FileCopyrightText: 2026 Project Dumont
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Content.Goobstation.Shared.Xenobiology.Components;
using Content.Goobstation.Shared.Xenobiology.Systems;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Goobstation.Xenobiology;

[TestFixture]
public sealed class SlimeMitosisTest
{
    [Test]
    public async Task MultipleSlimesCanDivideDuringTheSameUpdate()
    {
        var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var parents = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            for (var i = 0; i < 2; i++)
            {
                var uid = entMan.SpawnEntity("MobSlimeXenobioBabyGrey", map.GridCoords);
                parents.Add(uid);
                entMan.GetComponent<MobGrowthComponent>(uid).CurrentStage = "adult";
                var slime = entMan.GetComponent<SlimeComponent>(uid);
                slime.MaxOffspring = 1;
                slime.MutationChance = 0;
                slime.NextUpdateTime = TimeSpan.Zero;
                entMan.System<HungerSystem>().SetHunger(uid, slime.MitosisHunger + 10);
            }

            Assert.DoesNotThrow(() => entMan.System<XenobiologySystem>().Update(1f / 30f));
        });

        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            foreach (var parent in parents)
                Assert.That(entMan.EntityExists(parent), Is.False);

            var children = 0;
            var query = entMan.EntityQueryEnumerator<SlimeComponent, MobGrowthComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var growth, out var xform))
            {
                if (xform.GridUid != map.GridCoords.EntityId)
                    continue;

                Assert.That(growth.IsFirstStage, Is.True);
                children++;
            }

            Assert.That(children, Is.EqualTo(2));
        });

        await pair.CleanReturnAsync();
    }
}
