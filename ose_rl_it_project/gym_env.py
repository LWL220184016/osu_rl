import gymnasium as gym
import numpy as np
import cv2

from gymnasium import spaces
from model.config import model_config, train_config

class OsuEnv(gym.Env):
    """
    Gymnasium environment for the Balancing Ball game with continuous action space
    """

    def __init__(self,
                 render_mode: str = None,
                 model_cfg: model_config = None, 
                 train_cfg: train_config = None, 
                 ipcHandler = None,
                ):
        """
        envonment initialization
        Args:
            render_mode (str): The mode to render the game. Options are 'human', 'rgb_array', 'rgb_array_and_human_in_colab'.
            model_cfg (int): Config of model.
            train_cfg (int): Config of training.
            ipcHandler: for communicate with osu client
        """

        super(OsuEnv, self).__init__()
        print("Initializing BalancingBallEnv...")

        # Initialize game

        # Image preprocessing settings
        self.image_size = model_cfg.image_size 

        self.stack_size = model_cfg.obs_stack_size  # Number of frames to stack
        self.observation_stack = []  # Initialize the stack
        self.render_mode = render_mode

        # Action space: continuous - Box space for horizontal force [-1.0, 1.0] for each player
        self.action_space = model_cfg.action_space
        self.observation_space = model_cfg.observation_space

        if model_cfg.model_obs_type == "game_screen":
            self._preprocess_observation = self._preprocess_observation_game_screen
            self.step = self.step_game_screen
            self.reset = self.reset_game_screen

        elif model_cfg.model_obs_type == "state_based":
            raise NotImplementedError("functions of obs type = state_based is not implemented for osu! lazer")
        else:
            raise ValueError(f"obs_type: {model_cfg.model_obs_type} must be 'game_screen' or 'state_based'")

    def _preprocess_observation_game_screen(self, observation):
        """Process raw game observation for RL training

        Args:
            observation: RGB image from the game

        Returns:
            Processed observation ready for RL
        """
        
        observation = np.transpose(observation, (1, 0, 2))
        observation = cv2.cvtColor(observation, cv2.COLOR_RGB2GRAY)
        observation = np.expand_dims(observation, axis=-1)  # Add channel dimension back

        # Resize to target size
        if observation.shape[0] != self.image_size[0] or observation.shape[1] != self.image_size[1]:
            # For grayscale, temporarily remove the channel dimension for cv2.resize
            observation = cv2.resize(
                observation.squeeze(-1),
                (self.image_size[1], self.image_size[0]),
                interpolation=cv2.INTER_AREA
            )
            observation = np.expand_dims(observation, axis=-1)  # Add channel dimension back

        return observation

    def step_game_screen(self, action):
        """Take a step in the environment with continuous actions"""
        # Ensure action is the right shape

        # Take step in the game
        obs, step_rewards, terminated = self.game.step(action)

        # Preprocess the observation
        obs = self._preprocess_observation(obs)

        # Stack the frames
        self.observation_stack.append(obs)
        if len(self.observation_stack) > self.stack_size:
            self.observation_stack.pop(0)  # Remove the oldest frame

        # If the stack isn't full yet, pad it with the current frame
        while len(self.observation_stack) < self.stack_size:
            self.observation_stack.insert(0, obs)  # Pad with current frame at the beginning

        stacked_obs = np.concatenate(self.observation_stack, axis=-1)

        # For multi-agent, return sum of rewards or individual rewards based on your preference
        # Here we return the sum for single-agent training on multi-player game
        total_reward = sum(step_rewards) if isinstance(step_rewards, list) else step_rewards

        # Gymnasium expects (observation, reward, terminated, truncated, info)
        info = {
            'individual_rewards': step_rewards if isinstance(step_rewards, list) else [step_rewards],
            'winner': getattr(self.game, 'winner', None),
            'scores': getattr(self.game, 'score', [0])
        }

        return stacked_obs, total_reward, terminated, False, info

    def reset_game_screen(self, seed=None, options=None):
        """Reset the environment"""
        super().reset(seed=seed)  # This properly seeds the environment in Gymnasium

        observation = self.game.reset()

        # Preprocess the observation
        observation = self._preprocess_observation(observation)

        # Reset the observation stack
        self.observation_stack = []

        # Fill the stack with the initial observation
        for _ in range(self.stack_size):
            self.observation_stack.append(observation)

        # Create stacked observation
        stacked_obs = np.concatenate(self.observation_stack, axis=-1)

        info = {}
        return stacked_obs, info

    def render(self):
        """Render the environment"""
        return self.game.render()

    def close(self):
        """Clean up resources"""
        self.game.close()

