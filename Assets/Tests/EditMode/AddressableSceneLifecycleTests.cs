using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZZZ.ResourceManagement;

namespace ZZZ.Tests
{
    public sealed class AddressableSceneLifecycleTests
    {
        [UnityTest]
        public IEnumerator StoppingPlayModeWithLoadedSquadDoesNotCreateCleanupObjects()
        {
            for (int iteration = 0; iteration < 2; iteration++)
            {
                yield return new EnterPlayMode();
                yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
                Type squadType = Type.GetType("ZZZ.Player.SquadController, Assembly-CSharp", true);
                UnityEngine.Object squad = UnityEngine.Object.FindFirstObjectByType(squadType);
                Assert.IsNotNull(squad);
                PropertyInfo ready = squadType.GetProperty("IsReady");
                float deadline = Time.realtimeSinceStartup + 40f;
                while (!(bool)ready.GetValue(squad) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.IsTrue((bool)ready.GetValue(squad), squadType.GetProperty("LoadError").GetValue(squad) as string);
                // Exit with active owners, without unloading the scene or explicitly shutting down the squad.
                yield return new ExitPlayMode();
                Assert.IsNull(UnityEngine.Object.FindFirstObjectByType<ResourceLifetime>());
            }
        }

        [UnityTest]
        public IEnumerator RepeatedCombatEntryAndExitReleasesAllPrefabHandles()
        {
            yield return new EnterPlayMode();
            Type squadType = Type.GetType("ZZZ.Player.SquadController, Assembly-CSharp", true);
            for (int iteration = 0; iteration < 2; iteration++)
            {
                yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
                UnityEngine.Object squad = UnityEngine.Object.FindFirstObjectByType(squadType);
                Assert.IsNotNull(squad);
                PropertyInfo ready = squadType.GetProperty("IsReady");
                float deadline = Time.realtimeSinceStartup + 40f;
                while (!(bool)ready.GetValue(squad) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.IsTrue((bool)ready.GetValue(squad), squadType.GetProperty("LoadError").GetValue(squad) as string);
                Assert.IsNotNull(squadType.GetProperty("ActiveAgent").GetValue(squad));
                Assert.Greater(AddressableResources.RetainedPrefabCount, 1);

                Type serviceType = Type.GetType("ZZZ.Effects.EffectService, Assembly-CSharp", true);
                Type handleType = Type.GetType("ZZZ.Effects.PooledEffectHandle, Assembly-CSharp", true);
                var borrowed = new List<GameObject>();
                foreach (object pool in (IEnumerable)serviceType.GetProperty("DebugPools").GetValue(null))
                {
                    var instance = (GameObject)pool.GetType().GetMethod("Get").Invoke(pool, null);
                    if (instance.GetComponent(handleType) == null) instance.AddComponent(handleType);
                    instance.SetActive(true);
                    borrowed.Add(instance);
                }
                Assert.IsNotEmpty(borrowed);

                var shutdown = (Task)squadType.GetMethod("ShutdownAsync").Invoke(squad, null);
                deadline = Time.realtimeSinceStartup + 40f;
                while (!shutdown.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(shutdown.IsCompleted);
                Assert.IsFalse(shutdown.IsFaulted);
                Scene empty = SceneManager.CreateScene($"ResourceCleanup{iteration}");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync("SampleScene");
                while (AddressableResources.RetainedPrefabCount != 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.AreEqual(0, AddressableResources.RetainedPrefabCount);
                foreach (GameObject instance in borrowed) Assert.IsTrue(instance == null);
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator LeavingWhileSquadIsLoadingReleasesLateResults()
        {
            yield return new EnterPlayMode();
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            Scene empty = SceneManager.CreateScene("CancelledPreparation");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("SampleScene");
            float deadline = Time.realtimeSinceStartup + 40f;
            while (AddressableResources.RetainedPrefabCount != 0 && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(0, AddressableResources.RetainedPrefabCount);
            yield return new ExitPlayMode();
        }
    }
}
