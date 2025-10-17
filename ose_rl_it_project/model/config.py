
import pathlib
import torch
import numpy as np
# <<< 導入正確的 SAC 策略網路 >>>
# MlpPolicy 用於狀態輸入 (state_based)
# CnnPolicy 用於圖像輸入 (game_screen)
from stable_baselines3 import SAC
from stable_baselines3.sac.policies import MlpPolicy, CnnPolicy
from gymnasium import spaces

class model_config:
    fps=60
    rl_algorithm = SAC

    action_space = spaces.Tuple((
        spaces.Box(low=0.1, high=0.9, shape=(2,), dtype=np.float32),  # 兩個連續動作
        spaces.Discrete(2)  # 一個離散動作 (例如 0~4)
    ))


    model_obs_type="game_screen"
    obs_stack_size = 4

    # --- 策略網路 (Policy Kwargs) ---
    # SAC 的策略網路結構通常不需要像 PPO 那樣深，因為 SAC 的學習過程更穩定
    # 256x256 是一個非常常見且穩健的 SAC 網路結構
    if model_obs_type == "game_screen":
        policy_kwargs={
            "features_extractor_kwargs": {"features_dim": 256},
            "net_arch": [256, 256], # 對於 SAC，256x256 是常見的穩健選擇
            "activation_fn": torch.nn.ReLU,
        }
        image_size=(84, 84)

        # Image observation space with stacked frames
        channels = 1 # Gray
        observation_space = spaces.Box(
            low=0, high=255,
            shape=(image_size[0], image_size[1], channels * obs_stack_size),
            dtype=np.uint8,
        )

    elif model_obs_type == "state_based":
        policy_kwargs={
            "net_arch": [256, 256], # 對於 SAC，256x256 是常見的穩健選擇
            "activation_fn": torch.nn.ReLU, # ReLU 在 SAC 中更常用且表現穩定
        }
        obs_size = 12


    # --- SAC 模型核心參數 (model_param) ---
    model_param={
        # <<< 1. policy >>>
        # 根據觀測類型選擇 SAC 對應的策略
        "policy": MlpPolicy if model_obs_type == "state_based" else CnnPolicy,  

        # <<< 2. learning_rate >>>
        # SAC 通常使用比 PPO 更高的學習率，因為其學習過程更穩定。
        # 3e-4 (0.0003) 是一個非常經典和常用的 SAC 學習率
        # 這裡依然可以使用我們之前定義的學習率排程函數
        "learning_rate": 3e-4, 
        
        # <<< 3. buffer_size >>>
        # Replay Buffer 的大小，這是 Off-Policy 算法的核心。
        # 儲存過去的經驗。越大的 buffer 穩定性越好，但記憶體消耗也越大。
        # 1,000,000 是一個常見的選擇。
        "buffer_size": 1_000_000,

        # <<< 4. learning_starts >>>
        # 在訓練開始前，先隨機探索多少步來填充 Replay Buffer。
        # 這可以確保初始的訓練數據具有多樣性。
        # 1000 到 10000 都是常見值。
        "learning_starts": 10000,

        # <<< 5. batch_size >>>
        # 每次從 Replay Buffer 中採樣多少數據來進行一次梯度更新。
        # 256 是一個非常標準的 SAC batch size。
        "batch_size": 256,

        # <<< 6. tau >>>
        # "軟更新" (Soft Update) 係數，用於更新目標網絡 (Target Network)。
        # 每次只將主網絡的一小部分權重 (tau) 混合到目標網絡中。
        # 這使得目標 Q 值的變化更平滑，增加了訓練穩定性。0.005 是標準值。
        "tau": 0.005,

        # <<< 7. gamma >>>
        # 折扣因子，與 PPO 意義相同。0.99 是一個更常見的 SAC 設置。
        "gamma": 0.99,

        # <<< 8. train_freq >>>
        # 訓練頻率。SAC 可以控制是按「步 (step)」還是按「回合 (episode)」來觸發訓練。
        # (1, "step") 表示每在環境中執行 1 步，就進行 1 次梯度更新。
        # 這是最常見的設置，可以最大化樣本效率。
        "train_freq": (1, "step"),

        # <<< 9. gradient_steps >>>
        # 在每次觸發訓練時，執行多少次梯度更新。
        # 默認為 -1，在 `train_freq` 為 (n, "step") 時，這意味著每 n 步也執行 n 次更新。
        # 設置為 1 意味著每 1 步，執行 1 次更新。
        "gradient_steps": 1,
        
        # <<< 10. ent_coef >>>
        # 熵係數。在 SAC 中，強烈建議設置為 'auto'。
        # 這會讓 SAC 自動學習一個 alpha 參數來平衡獎勵和熵，
        # 是解決您之前遇到的探索/利用失衡問題的關鍵！
        "ent_coef": 'auto',

        # <<< 11. policy_kwargs >>>
        "policy_kwargs": policy_kwargs,
        "verbose": 1,
    }

class train_config:
    total_timesteps=5000000
    max_episode_step=500000  # Maximum steps per episode
    save_freq=50000
    eval_freq=10000
    eval_episodes=5
    tensorboard_log="./logs/"
    model_dir="./models/"
    render_mode="headless"  

    # msg = """
    # render_mode = "human" Suitable for testing models on a local computer, and can display the game screen while the model is playing the game
    # render_mode = "headless" Suitable for training models on Google Colab, significantly reducing computational load and speeding up training
    # """
    # print(f"\n\033[38;5;220m {msg}\033[0m")
    