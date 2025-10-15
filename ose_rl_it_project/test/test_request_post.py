import os
import sys
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

import time
from request import request_post

data = {
    "click": True
}
for i in range(10):
    time.sleep(1)
    request_post(data)