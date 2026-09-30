using UnityEngine;

namespace HotReload.Runtime
{
    /// <summary>
    /// Demo script demonstrating Custom Hot Reload features:
    /// 1. Method detouring: Edit CalculateScore() live.
    /// 2. Property detouring: Edit BonusMultiplier live.
    /// 3. [HotReloaded] callback: Runs immediately upon hot reload.
    /// 4. Dynamic Fields: Add new variables on the fly using this.SetDynamicField().
    /// </summary>
    public class HotReloadDemo : MonoBehaviour
    {
        [Header("Hot Reload Test Settings")]
        [SerializeField] private string targetName = "ZombieCar";
        [SerializeField] private float speed = 25f;

        private float _timer;

        // --- PROPERTY HOT RELOAD ---
        public float BonusMultiplier
        {
            get => 1.5f; // Try changing this to 3.0f live!
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= 2.0f)
            {
                _timer = 0f;
                ExecuteTick();
            }
        }

        public void ExecuteTick()
        {
            float score = CalculateScore(speed);
            string message = GetMessage();

            // Dynamic field usage: Access state not present in the original class layout
            int reloadCount = this.GetDynamicField<int>("HotReloadCounter", 0);

            Debug.Log($"<color=#50fa7b>[HotReloadDemo]</color> {targetName} | {message} | Score: {score} | Reloads: {reloadCount}");
        }

        // --- METHOD HOT RELOAD ---
        public float CalculateScore(float baseSpeed)
        {
            return baseSpeed * BonusMultiplier;
        }

        public string GetMessage()
        {
            return "Running version 1.0!";
        }

        // --- LIFECYCLE CALLBACK ---
        [HotReloaded]
        public void OnReloaded()
        {
            int count = this.GetDynamicField<int>("HotReloadCounter", 0) + 1;
            this.SetDynamicField("HotReloadCounter", count);

            Debug.Log($"<color=#ff79c6>[HotReload Callback]</color> <b>{name}</b> detected hot reload! (Total reloads: {count})");
        }
    }
}
