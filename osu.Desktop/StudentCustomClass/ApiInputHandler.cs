// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// this file mainly copied from osu.Framework.Input.Handlers.Mouse

#nullable disable

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using osu.Framework;
using osu.Framework.Bindables;
using osu.Framework.Platform;
using osu.Framework.Statistics;
using osuTK;
using osuTK.Input;
using Sentry.Protocol;

using osu.Framework.Input.Handlers;
using osu.Framework.Input.StateChanges;
using System.Collections.Generic;

namespace osu.Desktop.StudentCustomClass
{
    public class ApiInputHandler : InputHandler
    {
        private static readonly GlobalStatistic<ulong> statistic_total_events = GlobalStatistics.Get<ulong>(StatisticGroupFor<ApiInputHandler>(), "Total events");

        /// <summary>
        /// Whether relative mode should be preferred when the window has focus, the cursor is contained and the OS cursor is not visible.
        /// </summary>
        public BindableBool UseRelativeMode { get; } = new BindableBool(true)
        {
            Description = "Allows for sensitivity adjustment and tighter control of input",
        };

        public BindableDouble Sensitivity { get; } = new BindableDouble(1)
        {
            MinValue = 0.1,
            MaxValue = 10,
            Precision = 0.01
        };

        public ApiInputHandler()
        {

        }


        public override string Description => "APIInput";

        public override bool IsActive => true;

        private Vector2? lastPosition;

        public ConcurrentQueue<IInput> PerformAction(JsonObject actionObject)
        {
            //bool isActionHandled = false;
            // 檢查 'click' 屬性
            if (actionObject.TryGetPropertyValue("mouseButtonDown", out var isDown))
            {
                if (isDown != null && isDown.GetValue<JsonElement>().ValueKind == JsonValueKind.True)
                {
                    MouseButton mb = MouseButton.Left;
                    handleMouseDown(mb);
                    //isActionHandled = true;
                }
            }
            else if (actionObject.TryGetPropertyValue("mouseButtonUp", out var isUp))
            {
                if (isUp != null && isUp.GetValue<JsonElement>().ValueKind == JsonValueKind.True)
                {
                    MouseButton mb = MouseButton.Left;
                    handleMouseUp(mb);
                    //isActionHandled = true;
                }
            }
            // 檢查 'move' 物件
            else if (actionObject.TryGetPropertyValue("move", out var moveNode))
            {
                if (moveNode is not JsonObject moveObject)
                {
                    //await sendErrorResponse(response, HttpStatusCode.BadRequest, "'move' 屬性的值必須是一個包含 x 和 y 的物件。");
                    return PendingInputs;
                }

                //if (moveObject.TryGetPropertyValue("x", out var xNode) && xNode.GetValue<JsonElement>().TryGetInt32(out int x) &&
                //    moveObject.TryGetPropertyValue("y", out var yNode) && yNode.GetValue<JsonElement>().TryGetInt32(out int y))
                //{
                //var pos = (x, y);
                Vector2 pos = moveObject.GetValue<Vector2>();
                HandleMouseMove(pos);
                //isActionHandled = true;
                //}
                //else
                //{
                //    //await sendErrorResponse(response, HttpStatusCode.BadRequest, "'move' 物件必須包含型別為整數的 'x' 和 'y' 屬性。");
                //    return false; ;
                //}
            }

            return PendingInputs;
        }


        public override void Reset()
        {
            Sensitivity.SetDefault();
            base.Reset();
        }


        protected virtual void HandleMouseMove(Vector2 position)
        {
            enqueueInput(new MousePositionAbsoluteInput { Position = position });
        }

        protected virtual void HandleMouseMoveRelative(Vector2 delta)
        {
            enqueueInput(new MousePositionRelativeInput { Delta = delta * (float)Sensitivity.Value });
        }

        private void handleMouseDown(MouseButton button) => enqueueInput(new MouseButtonInput(button, true));

        private void handleMouseUp(MouseButton button) => enqueueInput(new MouseButtonInput(button, false));

        private void handleMouseWheel(Vector2 delta, bool precise) => enqueueInput(new MouseScrollRelativeInput { Delta = delta, IsPrecise = precise });

        private void enqueueInput(IInput input)
        {
            PendingInputs.Enqueue(input);
            statistic_total_events.Value++;
        }

    }
}

