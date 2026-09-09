import unittest
import numpy as np
import pandas as pd
from action_prediction.sequence import event_targets, risk_rows, metrics


class SequenceTests(unittest.TestCase):
    def frame(self, actions, frames=None):
        return pd.DataFrame(dict(match_id=['m']*len(actions), player_id=[1]*len(actions),
                                 frame=frames or list(range(len(actions))), self_action_id=actions))

    def test_first_exit_not_endpoint(self):
        t = event_targets(self.frame([0, 0, 100, 0]))
        self.assertEqual(t.duration.tolist(), [2, 1, 1, 0])
        self.assertEqual(t.destination.tolist(), [3, 3, 0, -1])
        self.assertEqual(t.observed.tolist(), [True, True, True, False])

    def test_censoring_no_fake_exit(self):
        t = event_targets(self.frame([0, 0, 0]))
        rows, steps, y, _ = risk_rows(t, np.arange(3))
        self.assertEqual(y.tolist(), [0, 0, 0])
        self.assertEqual(rows.tolist(), [0, 0, 1])
        self.assertEqual(steps.tolist(), [1, 2, 1])

    def test_gaps_and_forced_stop_observation(self):
        t = event_targets(self.frame([0, 100, 200, 0], [0, 2, 3, 4]))
        self.assertFalse(t.observed.any())
        self.assertEqual(t.duration.tolist(), [0, 0, 0, 0])

    def test_sessions_never_link(self):
        f = self.frame([0, 100])
        f.loc[1, 'match_id'] = 'other'
        self.assertEqual(event_targets(f).duration.tolist(), [0, 0])

    def test_no_changes_report_null(self):
        result = metrics(np.array([0]), np.array([0]), np.array([0]))
        self.assertIsNone(result['change_accuracy'])
        self.assertEqual(result['false_alarm_rate'], 0)


if __name__ == '__main__':
    unittest.main()
