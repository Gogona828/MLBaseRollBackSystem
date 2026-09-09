import unittest
import numpy as np
import pandas as pd
from action_prediction.sequence_experiments import paths_for, rollout, sequence_metrics, choose


class FakeModel:
    def predict(self, x, current, k=1):
        p = np.zeros((len(x), 5))
        p[np.arange(len(x)), current] = .1
        p[np.arange(len(x)), (current + 1) % 5] = .9
        return p


class ExperimentTests(unittest.TestCase):
    def test_return_to_start_is_two_changes(self):
        m = sequence_metrics(np.array([0]), np.array([[1, 0, 0]]), np.array([[0, 0, 0]]))
        self.assertEqual(m['actual_events'], 2)
        self.assertEqual(m['missed_events'], 2)
        self.assertEqual(m['changed_paths'], 1)
        self.assertIsNone(m['false_alarm_path_rate'])

    def test_event_match_is_one_to_one(self):
        m = sequence_metrics(np.array([0]), np.array([[1, 0, 0]]), np.array([[1, 1, 1]]))
        self.assertEqual(m['matched_events'], 1)
        self.assertEqual(m['missed_events'], 1)

    def test_wrong_destination_separate_from_timing(self):
        m = sequence_metrics(np.array([0]), np.array([[1, 1, 1]]), np.array([[2, 2, 2]]))
        self.assertEqual(m['timing_mae_frames'], 0)
        self.assertEqual(m['wrong_destinations'], 1)
        self.assertEqual(m['mean_longest_error_run'], 3)

    def test_multiswitch_rollout_does_not_mutate_input(self):
        x = np.zeros((2, 7), np.float32)
        before = x.copy()
        paths, scores = rollout(FakeModel(), x, 3, 3)
        np.testing.assert_equal(x, before)
        np.testing.assert_equal(paths[:, 0], [[1, 2, 3], [1, 2, 3]])
        self.assertTrue(np.all(np.diff(scores, axis=1) <= 0))

    def test_rollout_batch_and_single_identical(self):
        x = np.zeros((2, 7), np.float32)
        x[1, 0] = 3
        paths, _ = rollout(FakeModel(), x, 6, 3)
        for i in range(2):
            one, _ = rollout(FakeModel(), x[i:i+1], 6, 3)
            np.testing.assert_equal(paths[i], one[0])

    def test_paths_exclude_forced_intermediate(self):
        f = pd.DataFrame(dict(match_id=['a']*5, player_id=[1]*5, session_id=['s']*5,
                              frame=range(5), self_action_id=[0, 200, 0, 0, 0]))
        p, excluded = paths_for(f, {'s': 'test'}, 'test', 2)
        np.testing.assert_equal(p, [[2, 3, 4]])
        self.assertEqual(excluded, 2)

    def test_validation_constraint(self):
        rows = [dict(false_alarm_path_rate=.2, near_change_accuracy=.8, frame_accuracy=.9),
                dict(false_alarm_path_rate=.05, near_change_accuracy=.3, frame_accuracy=.7)]
        self.assertIs(choose(rows, .1), rows[1])


if __name__ == '__main__':
    unittest.main()
