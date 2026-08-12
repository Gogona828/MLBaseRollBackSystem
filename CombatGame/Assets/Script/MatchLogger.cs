using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Footsies
{
    /// <summary>
    /// BattleScene の1ラウンドを1試合として保存するロガー。
    /// オフラインCPU戦では人間(P1)だけを教師データ行として保存し、
    /// オンライン戦では両プレイヤーとネットワーク固有情報を保存する。
    /// </summary>
    internal sealed class MatchLogger : IDisposable
    {
        private const float CornerMargin = 0.25f;
        private const float WarpThreshold = 0.25f;

        private readonly BattleCore battleCore;
        private readonly FootsiesBattleInputRouter inputRouter;
        private readonly NetworkInputReceiver inputReceiver;
        private readonly NetworkFrameClock frameClock;
        private readonly PredictionMismatchDetector predictionDetector;
        private readonly string sessionId = Guid.NewGuid().ToString("N");

        private readonly List<FrameRecord> frameRecords = new List<FrameRecord>();
        private readonly List<CombatEventRecord> eventRecords = new List<CombatEventRecord>();
        private readonly List<NetworkPacketRecord> networkRecords = new List<NetworkPacketRecord>();
        private readonly List<PredictionLogRecord> predictionRecords = new List<PredictionLogRecord>();
        private readonly List<RollbackLogRecord> rollbackRecords = new List<RollbackLogRecord>();
        private readonly Dictionary<int, int> simulationPassByFrame = new Dictionary<int, int>();
        private readonly object callbackLock = new object();

        private bool roundActive;
        private bool isOffline;
        private int roundId;
        private string matchId;
        private string matchDirectory;
        private DateTimeOffset matchStartedAt;

        public bool IsRoundActive => roundActive;

        public MatchLogger(
            BattleCore battleCore,
            FootsiesBattleInputRouter inputRouter,
            NetworkInputReceiver inputReceiver,
            NetworkFrameClock frameClock,
            PredictionMismatchDetector predictionDetector)
        {
            this.battleCore = battleCore;
            this.inputRouter = inputRouter;
            this.inputReceiver = inputReceiver;
            this.frameClock = frameClock;
            this.predictionDetector = predictionDetector;

            if (this.inputReceiver != null)
            {
                this.inputReceiver.InputPacketReceived += OnInputPacketReceived;
            }

            if (this.predictionDetector != null)
            {
                this.predictionDetector.PredictionEvaluated += OnPredictionEvaluated;
            }
        }

        public void BeginRound(bool offline)
        {
            if (roundActive)
            {
                return;
            }

            isOffline = offline;
            roundId++;
            matchStartedAt = DateTimeOffset.Now;

            string baseDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "MatchLogs"));
            Directory.CreateDirectory(baseDirectory);

            string startTimeName = matchStartedAt.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            matchDirectory = GetUniqueDirectoryPath(baseDirectory, startTimeName);
            Directory.CreateDirectory(matchDirectory);
            matchId = Path.GetFileName(matchDirectory);

            frameRecords.Clear();
            eventRecords.Clear();
            networkRecords.Clear();
            predictionRecords.Clear();
            rollbackRecords.Clear();
            simulationPassByFrame.Clear();
            roundActive = true;
        }

        public void RecordFrame(
            int frame,
            int networkFrame,
            bool isResimulation,
            double frameProcessingTimeMs)
        {
            if (!roundActive || frame < 0)
            {
                return;
            }

            int simulationPass = GetNextSimulationPass(frame);
            FighterState p1 = CaptureFighterState(battleCore.fighter1);
            FighterState p2 = CaptureFighterState(battleCore.fighter2);
            float distanceX = p2.PositionX - p1.PositionX;
            float relativeVelocity = GetWorldVelocity(battleCore.fighter2) - GetWorldVelocity(battleCore.fighter1);

            AddFrameRecord(
                1,
                "human",
                isOffline ? "human" : GetInputSource(1, isResimulation),
                battleCore.fighter1,
                frame,
                networkFrame,
                simulationPass,
                isResimulation,
                frameProcessingTimeMs,
                p1,
                p2,
                distanceX,
                relativeVelocity);

            // ルールベースCPU自身の入力・行動を教師データ行としては保存しない。
            if (!isOffline)
            {
                AddFrameRecord(
                    2,
                    "human",
                    GetInputSource(2, isResimulation),
                    battleCore.fighter2,
                    frame,
                    networkFrame,
                    simulationPass,
                    isResimulation,
                    frameProcessingTimeMs,
                    p1,
                    p2,
                    distanceX,
                    relativeVelocity);
            }
        }

        public void RecordCombatEvent(
            Fighter actor,
            Fighter target,
            int attackId,
            DamageResult damageResult,
            int hitStunFrames,
            int healthBefore,
            int guardBefore)
        {
            if (!roundActive)
            {
                return;
            }

            int actorId = actor == battleCore.fighter1 ? 1 : 2;
            int targetId = target == battleCore.fighter1 ? 1 : 2;
            int simulationPass = PeekNextSimulationPass(battleCore.CurrentFrameCount);
            string eventType = GetDamageEventType(damageResult);

            eventRecords.Add(new CombatEventRecord
            {
                Timestamp = GetFrameTimestamp(battleCore.CurrentFrameCount),
                Frame = battleCore.CurrentFrameCount,
                NetworkFrame = GetCurrentNetworkFrame(),
                SimulationPass = simulationPass,
                IsResimulation = battleCore.IsResimulating,
                EventType = eventType,
                ActorPlayerId = actorId,
                TargetPlayerId = targetId,
                AttackId = attackId,
                DamageResult = damageResult.ToString(),
                HitStunFrames = hitStunFrames,
                HealthBefore = healthBefore,
                HealthAfter = target.vitalHealth,
                GuardBefore = guardBefore,
                GuardAfter = target.guardHealth
            });

            if (target.isDead)
            {
                eventRecords.Add(new CombatEventRecord
                {
                    Timestamp = GetFrameTimestamp(battleCore.CurrentFrameCount),
                    Frame = battleCore.CurrentFrameCount,
                    NetworkFrame = GetCurrentNetworkFrame(),
                    SimulationPass = simulationPass,
                    IsResimulation = battleCore.IsResimulating,
                    EventType = "ko",
                    ActorPlayerId = actorId,
                    TargetPlayerId = targetId,
                    AttackId = attackId,
                    DamageResult = damageResult.ToString(),
                    HitStunFrames = hitStunFrames,
                    HealthBefore = healthBefore,
                    HealthAfter = target.vitalHealth,
                    GuardBefore = guardBefore,
                    GuardAfter = target.guardHealth
                });
            }
        }

        public void RecordRollback(
            int rollbackTargetFrame,
            int rollbackFromFrame,
            byte predictedBits,
            byte confirmedBits,
            double restoreTimeMs,
            double resimulationTimeMs,
            float p1PositionBefore,
            float p2PositionBefore)
        {
            if (!roundActive || isOffline)
            {
                return;
            }

            float maxPositionDelta = Mathf.Max(
                Mathf.Abs(battleCore.fighter1.position.x - p1PositionBefore),
                Mathf.Abs(battleCore.fighter2.position.x - p2PositionBefore));
            int rollbackFrames = Mathf.Max(0, rollbackFromFrame - rollbackTargetFrame);
            int resimulationFrames = rollbackFrames + 1;
            bool rollbackWasLate = rollbackFrames > 1;

            rollbackRecords.Add(new RollbackLogRecord
            {
                Timestamp = DateTimeOffset.Now,
                NetworkFrame = rollbackFromFrame,
                PredictionMiss = predictedBits != confirmedBits,
                PredictedBits = predictedBits,
                ConfirmedBits = confirmedBits,
                RollbackRequested = true,
                RollbackTargetFrame = rollbackTargetFrame,
                RollbackFromFrame = rollbackFromFrame,
                RollbackToFrame = rollbackFromFrame,
                RollbackFrames = rollbackFrames,
                ResimulationFrames = resimulationFrames,
                WarpDetected = maxPositionDelta >= WarpThreshold,
                GhostHitCandidate = (predictedBits & (byte)InputDefine.Attack) != 0
                    && (confirmedBits & (byte)InputDefine.Attack) == 0
                    && rollbackWasLate,
                PredictionTimeMs = null,
                MlCpuInferenceTimeMs = null,
                RestoreTimeMs = restoreTimeMs,
                ResimulationTimeMs = resimulationTimeMs
            });
        }

        public void EndRound(string result)
        {
            if (!roundActive)
            {
                return;
            }

            roundActive = false;

            try
            {
                WriteMatches(result);
                WriteFrames();
                WriteEvents();

                if (!isOffline)
                {
                    WriteNetwork();
                    WritePredictions();
                    WriteRollback();
                }

                Debug.Log($"[MatchLogger] Match log written: {matchDirectory}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MatchLogger] Failed to write match log: {exception}");
            }
        }

        public void Dispose()
        {
            if (roundActive)
            {
                EndRound("aborted");
            }

            if (inputReceiver != null)
            {
                inputReceiver.InputPacketReceived -= OnInputPacketReceived;
            }

            if (predictionDetector != null)
            {
                predictionDetector.PredictionEvaluated -= OnPredictionEvaluated;
            }
        }

        private void AddFrameRecord(
            int playerId,
            string playerType,
            string inputSource,
            Fighter fighter,
            int frame,
            int networkFrame,
            int simulationPass,
            bool isResimulation,
            double frameProcessingTimeMs,
            FighterState p1,
            FighterState p2,
            float distanceX,
            float relativeVelocity)
        {
            frameRecords.Add(new FrameRecord
            {
                Timestamp = GetFrameTimestamp(frame),
                Frame = frame,
                NetworkFrame = networkFrame,
                SimulationPass = simulationPass,
                IsResimulation = isResimulation,
                FixedDeltaTime = Time.fixedDeltaTime,
                FrameProcessingTimeMs = frameProcessingTimeMs,
                PlayerId = playerId,
                PlayerType = playerType,
                InputBits = fighter.currentInputBits,
                InputDownBits = fighter.currentInputDownBits,
                InputUpBits = fighter.currentInputUpBits,
                InputSource = inputSource,
                P1 = p1,
                P2 = p2,
                DistanceX = distanceX,
                AbsoluteDistance = Mathf.Abs(distanceX),
                RelativeVelocity = relativeVelocity,
                P1Cornered = IsCornered(battleCore.fighter1),
                P2Cornered = IsCornered(battleCore.fighter2),
                RoundState = battleCore.roundState.ToString(),
                LatestReceivedRemoteFrame = inputReceiver != null
                    ? inputReceiver.GetLatestReceivedRemoteFrame()
                    : -1,
                LatestConfirmedRemoteFrame = inputReceiver != null
                    ? inputReceiver.GetLatestContiguousConfirmedRemoteFrame()
                    : -1,
                RemoteInputConfirmed = inputSource == "remote_confirmed",
                RemoteInputPredicted = inputSource == "remote_predicted"
            });
        }

        private void OnInputPacketReceived(InputPacket packet, int receivedAtFrame)
        {
            if (!roundActive || isOffline)
            {
                return;
            }

            lock (callbackLock)
            {
                networkRecords.Add(new NetworkPacketRecord
                {
                    Timestamp = DateTimeOffset.Now,
                    PacketFrame = packet.frame,
                    PacketPlayerId = packet.playerId + 1,
                    PacketInputBits = packet.inputBits,
                    PacketReceivedAtFrame = receivedAtFrame,
                    PacketDelayFrames = receivedAtFrame >= 0
                        ? receivedAtFrame - packet.frame
                        : -1,
                    LatestReceivedRemoteFrame = inputReceiver.GetLatestReceivedRemoteFrame(),
                    LatestConfirmedRemoteFrame = inputReceiver.GetLatestContiguousConfirmedRemoteFrame(),
                    RemoteInputConfirmed = true,
                    RemoteInputPredicted = false
                });
            }
        }

        private void OnPredictionEvaluated(PredictionRecord record)
        {
            if (!roundActive || isOffline)
            {
                return;
            }

            lock (callbackLock)
            {
                predictionRecords.Add(new PredictionLogRecord
                {
                    Timestamp = DateTimeOffset.Now,
                    NetworkFrame = record.Frame,
                    TargetPlayerId = inputRouter != null
                        ? inputRouter.GetPredictedRemotePlayerId() + 1
                        : 0,
                    PredictedBits = record.PredictedBits,
                    ConfirmedBits = record.ConfirmedBits,
                    Correct = record.ResultState == PredictionResultState.Hit,
                    PredictionMode = inputRouter != null
                        ? inputRouter.GetPredictionMode()
                        : string.Empty
                });
            }
        }

        private void WriteMatches(string result)
        {
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("match_id,round_id,session_id,timestamp,battle_mode,result,winner_player_id,player_id,player_type,controller_id,model_version,cpu_seed,git_commit");

            int winnerId = battleCore.fighter1.isDead && !battleCore.fighter2.isDead
                ? 2
                : battleCore.fighter2.isDead && !battleCore.fighter1.isDead
                    ? 1
                    : 0;
            string battleMode = isOffline ? "cpu" : "online";
            string gitCommit = ResolveGitCommit();

            AppendMatchRow(csv, 1, "human", isOffline ? "local_input" : "network_input", battleMode, result, winnerId, gitCommit);
            AppendMatchRow(csv, 2, isOffline ? "rule_cpu" : "human", isOffline ? "BattleAI" : "network_input", battleMode, result, winnerId, gitCommit);
            WriteCsv("matches.csv", csv);
        }

        private void AppendMatchRow(
            StringBuilder csv,
            int playerId,
            string playerType,
            string controllerId,
            string battleMode,
            string result,
            int winnerId,
            string gitCommit)
        {
            AppendRow(csv,
                matchId,
                roundId,
                sessionId,
                matchStartedAt.ToString("O", CultureInfo.InvariantCulture),
                battleMode,
                result,
                winnerId == 0 ? string.Empty : winnerId.ToString(CultureInfo.InvariantCulture),
                playerId,
                playerType,
                controllerId,
                string.Empty,
                string.Empty,
                gitCommit);
        }

        private void WriteFrames()
        {
            StringBuilder csv = new StringBuilder();
            if (isOffline)
            {
                csv.Append("match_id,round_id,timestamp,frame,fixed_delta_time,frame_processing_time_ms,");
            }
            else
            {
                csv.Append("match_id,round_id,timestamp,frame,network_frame,simulation_pass,is_resimulation,is_canonical,fixed_delta_time,frame_processing_time_ms,");
            }

            csv.Append("player_id,player_type,input_bits,input_left,input_right,input_attack,input_down_bits,input_up_bits,input_source,p1_position_x,p1_position_y,p1_velocity_x,p1_is_face_right,p1_vital_health,p1_guard_health,p1_is_dead,p1_action_id,p1_action_frame,p1_hitstun_frame,p1_is_in_hitstun,p1_buffer_action_id,p2_position_x,p2_position_y,p2_velocity_x,p2_is_face_right,p2_vital_health,p2_guard_health,p2_is_dead,p2_action_id,p2_action_frame,p2_hitstun_frame,p2_is_in_hitstun,p2_buffer_action_id,distance_x,absolute_distance,relative_velocity,p1_is_cornered,p2_is_cornered,round_state");
            if (!isOffline)
            {
                csv.Append(",latest_received_remote_frame,latest_confirmed_remote_frame,remote_input_confirmed,remote_input_predicted");
            }
            csv.AppendLine();

            foreach (FrameRecord record in frameRecords)
            {
                bool isCanonical = record.SimulationPass == simulationPassByFrame[record.Frame];
                List<object> fields = new List<object>
                {
                    matchId,
                    roundId,
                    record.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    record.Frame
                };

                if (!isOffline)
                {
                    fields.Add(record.NetworkFrame);
                    fields.Add(record.SimulationPass);
                    fields.Add(record.IsResimulation);
                    fields.Add(isCanonical);
                }

                fields.Add(record.FixedDeltaTime);
                fields.Add(record.FrameProcessingTimeMs);
                fields.Add(record.PlayerId);
                fields.Add(record.PlayerType);
                fields.Add(record.InputBits);
                fields.Add(HasInput(record.InputBits, InputDefine.Left));
                fields.Add(HasInput(record.InputBits, InputDefine.Right));
                fields.Add(HasInput(record.InputBits, InputDefine.Attack));
                fields.Add(record.InputDownBits);
                fields.Add(record.InputUpBits);
                fields.Add(record.InputSource);
                AddFighterStateFields(fields, record.P1);
                AddFighterStateFields(fields, record.P2);
                fields.Add(record.DistanceX);
                fields.Add(record.AbsoluteDistance);
                fields.Add(record.RelativeVelocity);
                fields.Add(record.P1Cornered);
                fields.Add(record.P2Cornered);
                fields.Add(record.RoundState);
                if (!isOffline)
                {
                    fields.Add(record.LatestReceivedRemoteFrame);
                    fields.Add(record.LatestConfirmedRemoteFrame);
                    fields.Add(record.RemoteInputConfirmed);
                    fields.Add(record.RemoteInputPredicted);
                }
                AppendRow(csv, fields.ToArray());
            }

            WriteCsv("frames.csv", csv);
        }

        private void WriteEvents()
        {
            StringBuilder csv = new StringBuilder();
            if (isOffline)
            {
                csv.AppendLine("match_id,round_id,timestamp,event_type,event_frame,actor_player_id,target_player_id,attack_id,damage_result,hitstun_frames,health_before,health_after,guard_before,guard_after");
            }
            else
            {
                csv.AppendLine("match_id,round_id,timestamp,event_type,event_frame,network_frame,simulation_pass,is_resimulation,is_canonical,actor_player_id,target_player_id,attack_id,damage_result,hitstun_frames,health_before,health_after,guard_before,guard_after");
            }

            foreach (CombatEventRecord record in eventRecords)
            {
                bool isCanonical = !simulationPassByFrame.ContainsKey(record.Frame)
                    || record.SimulationPass == simulationPassByFrame[record.Frame];
                List<object> fields = new List<object>
                {
                    matchId,
                    roundId,
                    record.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    record.EventType,
                    record.Frame
                };

                if (!isOffline)
                {
                    fields.Add(record.NetworkFrame);
                    fields.Add(record.SimulationPass);
                    fields.Add(record.IsResimulation);
                    fields.Add(isCanonical);
                }

                fields.Add(record.ActorPlayerId);
                fields.Add(record.TargetPlayerId);
                fields.Add(record.AttackId);
                fields.Add(record.DamageResult);
                fields.Add(record.HitStunFrames);
                fields.Add(record.HealthBefore);
                fields.Add(record.HealthAfter);
                fields.Add(record.GuardBefore);
                fields.Add(record.GuardAfter);
                AppendRow(csv, fields.ToArray());
            }

            WriteCsv("events.csv", csv);
        }

        private void WriteNetwork()
        {
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("match_id,round_id,timestamp,packet_frame,packet_player_id,packet_input_bits,packet_received_at_frame,packet_delay_frames,latest_received_remote_frame,latest_confirmed_remote_frame,remote_input_confirmed,remote_input_predicted");

            lock (callbackLock)
            {
                foreach (NetworkPacketRecord record in networkRecords)
                {
                    AppendRow(csv,
                        matchId,
                        roundId,
                        record.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                        record.PacketFrame,
                        record.PacketPlayerId,
                        record.PacketInputBits,
                        record.PacketReceivedAtFrame,
                        record.PacketDelayFrames,
                        record.LatestReceivedRemoteFrame,
                        record.LatestConfirmedRemoteFrame,
                        record.RemoteInputConfirmed,
                        record.RemoteInputPredicted);
                }
            }

            WriteCsv("network.csv", csv);
        }

        private void WritePredictions()
        {
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("match_id,round_id,timestamp,network_frame,ai_prediction_target,observation,cue,predicted_action,confirmed_action,prediction_correct,prediction_confidence,prediction_horizon,belief_aggressive,belief_defensive,belief_approaching,action_prob_wait,action_prob_forward,action_prob_backward,action_prob_attack,action_prob_guard,free_energy,expected_free_energy,predicted_input_bits,confirmed_input_bits,prediction_model");

            lock (callbackLock)
            {
                foreach (PredictionLogRecord record in predictionRecords)
                {
                    AppendRow(csv,
                        matchId,
                        roundId,
                        record.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                        record.NetworkFrame,
                        record.TargetPlayerId > 0 ? "P" + record.TargetPlayerId : string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        record.Correct,
                        string.Empty,
                        0,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        record.PredictedBits,
                        record.ConfirmedBits,
                        record.PredictionMode);
                }
            }

            WriteCsv("predictions.csv", csv);
        }

        private void WriteRollback()
        {
            StringBuilder csv = new StringBuilder();
            csv.AppendLine("match_id,round_id,timestamp,network_frame,prediction_miss,predicted_bits,confirmed_bits,rollback_requested,rollback_target_frame,rollback_from_frame,rollback_to_frame,rollback_frames,resimulation_frames,warp_detected,ghost_hit_candidate,prediction_time_ms,ml_cpu_inference_time_ms,rollback_restore_time_ms,resimulation_time_ms");

            foreach (RollbackLogRecord record in rollbackRecords)
            {
                AppendRow(csv,
                    matchId,
                    roundId,
                    record.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    record.NetworkFrame,
                    record.PredictionMiss,
                    record.PredictedBits,
                    record.ConfirmedBits,
                    record.RollbackRequested,
                    record.RollbackTargetFrame,
                    record.RollbackFromFrame,
                    record.RollbackToFrame,
                    record.RollbackFrames,
                    record.ResimulationFrames,
                    record.WarpDetected,
                    record.GhostHitCandidate,
                    record.PredictionTimeMs,
                    record.MlCpuInferenceTimeMs,
                    record.RestoreTimeMs,
                    record.ResimulationTimeMs);
            }

            WriteCsv("rollback.csv", csv);
        }

        private FighterState CaptureFighterState(Fighter fighter)
        {
            return new FighterState
            {
                PositionX = fighter.position.x,
                PositionY = fighter.position.y,
                VelocityX = fighter.velocity_x,
                IsFaceRight = fighter.isFaceRight,
                VitalHealth = fighter.vitalHealth,
                GuardHealth = fighter.guardHealth,
                IsDead = fighter.isDead,
                ActionId = fighter.currentActionID,
                ActionFrame = fighter.currentActionFrame,
                HitstunFrame = fighter.currentHitStunFrame,
                IsInHitstun = fighter.isInHitStun,
                BufferActionId = fighter.bufferedActionID
            };
        }

        private static void AddFighterStateFields(List<object> fields, FighterState state)
        {
            fields.Add(state.PositionX);
            fields.Add(state.PositionY);
            fields.Add(state.VelocityX);
            fields.Add(state.IsFaceRight);
            fields.Add(state.VitalHealth);
            fields.Add(state.GuardHealth);
            fields.Add(state.IsDead);
            fields.Add(state.ActionId);
            fields.Add(state.ActionFrame);
            fields.Add(state.HitstunFrame);
            fields.Add(state.IsInHitstun);
            fields.Add(state.BufferActionId);
        }

        private int GetNextSimulationPass(int frame)
        {
            int next = PeekNextSimulationPass(frame);
            simulationPassByFrame[frame] = next;
            return next;
        }

        private int PeekNextSimulationPass(int frame)
        {
            return simulationPassByFrame.TryGetValue(frame, out int current)
                ? current + 1
                : 1;
        }

        private string GetInputSource(int playerNumber, bool isResimulation)
        {
            if (isResimulation)
            {
                return "resimulation_override";
            }

            if ((playerNumber == 1 && (battleCore.debugP1Attack || battleCore.debugP1Guard))
                || (playerNumber == 2 && (battleCore.debugP2Attack || battleCore.debugP2Guard)))
            {
                return "debug_auto_input";
            }

            return inputRouter != null ? inputRouter.GetInputSource(playerNumber) : "unknown";
        }

        private int GetCurrentNetworkFrame()
        {
            return battleCore.IsResimulating
                ? battleCore.ResimulationNetworkFrame
                : frameClock != null
                    ? frameClock.CurrentFrame
                    : -1;
        }

        private DateTimeOffset GetFrameTimestamp(int frame)
        {
            return matchStartedAt.AddSeconds(Mathf.Max(0, frame) * Time.fixedDeltaTime);
        }

        private float GetWorldVelocity(Fighter fighter)
        {
            return fighter.velocity_x * (fighter.isFaceRight ? 1f : -1f);
        }

        private bool IsCornered(Fighter fighter)
        {
            if (fighter.pushbox == null)
            {
                return false;
            }

            float min = -battleCore.battleAreaWidth * 0.5f;
            float max = battleCore.battleAreaWidth * 0.5f;
            return fighter.pushbox.xMin <= min + CornerMargin
                || fighter.pushbox.xMax >= max - CornerMargin;
        }

        private static bool HasInput(int bits, InputDefine input)
        {
            return (bits & (int)input) != 0;
        }

        private static string GetDamageEventType(DamageResult damageResult)
        {
            switch (damageResult)
            {
                case DamageResult.Guard:
                    return "attack_guarded";
                case DamageResult.GuardBreak:
                    return "guard_break";
                default:
                    return "attack_hit";
            }
        }

        private static string GetUniqueDirectoryPath(string parent, string baseName)
        {
            string candidate = Path.Combine(parent, baseName);
            int suffix = 1;
            while (Directory.Exists(candidate))
            {
                candidate = Path.Combine(parent, baseName + "_" + suffix);
                suffix++;
            }

            return candidate;
        }

        private static string ResolveGitCommit()
        {
            string environmentCommit = Environment.GetEnvironmentVariable("GIT_COMMIT")
                ?? Environment.GetEnvironmentVariable("BUILD_VCS_NUMBER");
            if (!string.IsNullOrWhiteSpace(environmentCommit))
            {
                return environmentCommit.Trim();
            }

            try
            {
                DirectoryInfo directory = new DirectoryInfo(Application.dataPath);
                while (directory != null)
                {
                    string gitDirectory = Path.Combine(directory.FullName, ".git");
                    string headPath = Path.Combine(gitDirectory, "HEAD");
                    if (File.Exists(headPath))
                    {
                        string head = File.ReadAllText(headPath).Trim();
                        if (!head.StartsWith("ref: ", StringComparison.Ordinal))
                        {
                            return head;
                        }

                        string referenceName = head.Substring("ref: ".Length).Trim();
                        string referencePath = Path.Combine(
                            gitDirectory,
                            referenceName.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(referencePath))
                        {
                            return File.ReadAllText(referencePath).Trim();
                        }

                        string packedRefsPath = Path.Combine(gitDirectory, "packed-refs");
                        if (File.Exists(packedRefsPath))
                        {
                            foreach (string line in File.ReadAllLines(packedRefsPath))
                            {
                                if (line.StartsWith("#", StringComparison.Ordinal)
                                    || line.StartsWith("^", StringComparison.Ordinal))
                                {
                                    continue;
                                }

                                string suffix = " " + referenceName;
                                if (line.EndsWith(suffix, StringComparison.Ordinal))
                                {
                                    return line.Substring(0, line.Length - suffix.Length).Trim();
                                }
                            }
                        }

                        return string.Empty;
                    }

                    directory = directory.Parent;
                }
            }
            catch (Exception)
            {
                // ビルド成果物など .git が存在しない環境では空欄にする。
            }

            return string.Empty;
        }

        private void WriteCsv(string fileName, StringBuilder csv)
        {
            File.WriteAllText(Path.Combine(matchDirectory, fileName), csv.ToString(), new UTF8Encoding(false));
        }

        private static void AppendRow(StringBuilder csv, params object[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0)
                {
                    csv.Append(',');
                }

                csv.Append(EscapeCsv(FormatValue(fields[i])));
            }

            csv.AppendLine();
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is bool boolean)
            {
                return boolean ? "true" : "false";
            }

            if (value is float single)
            {
                return single.ToString("R", CultureInfo.InvariantCulture);
            }

            if (value is double number)
            {
                return number.ToString("R", CultureInfo.InvariantCulture);
            }

            if (value is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            return value.ToString();
        }

        private static string EscapeCsv(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        private sealed class FrameRecord
        {
            public DateTimeOffset Timestamp;
            public int Frame;
            public int NetworkFrame;
            public int SimulationPass;
            public bool IsResimulation;
            public float FixedDeltaTime;
            public double FrameProcessingTimeMs;
            public int PlayerId;
            public string PlayerType;
            public int InputBits;
            public int InputDownBits;
            public int InputUpBits;
            public string InputSource;
            public FighterState P1;
            public FighterState P2;
            public float DistanceX;
            public float AbsoluteDistance;
            public float RelativeVelocity;
            public bool P1Cornered;
            public bool P2Cornered;
            public string RoundState;
            public int LatestReceivedRemoteFrame;
            public int LatestConfirmedRemoteFrame;
            public bool RemoteInputConfirmed;
            public bool RemoteInputPredicted;
        }

        private struct FighterState
        {
            public float PositionX;
            public float PositionY;
            public float VelocityX;
            public bool IsFaceRight;
            public int VitalHealth;
            public int GuardHealth;
            public bool IsDead;
            public int ActionId;
            public int ActionFrame;
            public int HitstunFrame;
            public bool IsInHitstun;
            public int BufferActionId;
        }

        private sealed class CombatEventRecord
        {
            public DateTimeOffset Timestamp;
            public int Frame;
            public int NetworkFrame;
            public int SimulationPass;
            public bool IsResimulation;
            public string EventType;
            public int ActorPlayerId;
            public int TargetPlayerId;
            public int AttackId;
            public string DamageResult;
            public int HitStunFrames;
            public int HealthBefore;
            public int HealthAfter;
            public int GuardBefore;
            public int GuardAfter;
        }

        private sealed class NetworkPacketRecord
        {
            public DateTimeOffset Timestamp;
            public int PacketFrame;
            public int PacketPlayerId;
            public int PacketInputBits;
            public int PacketReceivedAtFrame;
            public int PacketDelayFrames;
            public int LatestReceivedRemoteFrame;
            public int LatestConfirmedRemoteFrame;
            public bool RemoteInputConfirmed;
            public bool RemoteInputPredicted;
        }

        private sealed class PredictionLogRecord
        {
            public DateTimeOffset Timestamp;
            public int NetworkFrame;
            public int TargetPlayerId;
            public byte PredictedBits;
            public byte ConfirmedBits;
            public bool Correct;
            public string PredictionMode;
        }

        private sealed class RollbackLogRecord
        {
            public DateTimeOffset Timestamp;
            public int NetworkFrame;
            public bool PredictionMiss;
            public byte PredictedBits;
            public byte ConfirmedBits;
            public bool RollbackRequested;
            public int RollbackTargetFrame;
            public int RollbackFromFrame;
            public int RollbackToFrame;
            public int RollbackFrames;
            public int ResimulationFrames;
            public bool WarpDetected;
            public bool GhostHitCandidate;
            public double? PredictionTimeMs;
            public double? MlCpuInferenceTimeMs;
            public double RestoreTimeMs;
            public double ResimulationTimeMs;
        }
    }
}
