from levels.rewards.reward_calculator import RewardComponent, terminates_round

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from game.role.player import Player
    from game.role.falling_rock import FallingRock
    from game.collision_handle import CollisionHandler

@terminates_round
class PlayerFallingRockCollisionReward(RewardComponent):
    """處理與實體 (如落石) 的碰撞獎勵/懲罰"""
    def calculate(self, 
                  players: list['Player'], 
                  falling_rocks: list['FallingRock'], 
                  collision_handler: 'CollisionHandler', 
                  window_x: int, 
                  window_y: int,
                  **kwargs
                 ):
                  
        
        for rock in falling_rocks:
            penalty = 0
            if rock.get_is_on_ground():
                penalty = self.falling_rock_fall_on_platform
                rock.reset()
                continue  # 如果石頭已經落地，跳過這次檢查

            pos_x, pos_y = rock.get_position()
            if pos_x < 0 or pos_x > window_x or pos_y > window_y: # 檢查是否掉出視窗底部或者落在平臺上
                collision_type = rock.get_last_collision_with()
                player = collision_handler.get_player_from_collision_type(collision_type)
                if player:
                    player.add_reward_per_step(self.falling_rock_fall_outside_platform)
                rock.reset()

        for player in players:
            if not player.get_is_alive():
                continue
            
            player.add_reward_per_step(penalty)
            if penalty < 0:
                
                if self._terminates_round:
                    if player.decrease_health(1) and player.get_health() <= 0:
                        player.set_is_alive(False)
                        continue  # 玩家死亡，處理下一個玩家
                    
            collision_list = player.get_collision_with()
            for collision in collision_list:
                if collision_handler.check_is_entities(collision): # 假設 falling_rock 屬於 entities
                    player.add_reward_per_step(self.collision_falling_rock)

