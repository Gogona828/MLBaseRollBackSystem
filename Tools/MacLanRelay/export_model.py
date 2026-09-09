"""Export the existing FEP + supervised XGBoost models for native-free Unity inference."""
import json
from pathlib import Path
import sys
import numpy as np
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'ML'))
from action_prediction.active_inference import load_active_inference_model
from action_prediction.features import FEATURE_COLUMNS

def export():
    source = ROOT / 'ML/artifacts/hybrid_experiments/models'
    fep = load_active_inference_model(source / 'fep.npz')
    result = {name: getattr(fep, name).ravel().tolist() for name in (
        'observation_likelihood', 'transition', 'initial_belief', 'habit_prior', 'utility', 'cue_likelihood')}
    result['featureNames'] = FEATURE_COLUMNS
    result['horizons'] = []
    for name, frames in [('50ms', 3), ('100ms', 6), ('200ms', 12)]:
        learner = json.loads((source / f'xgboost_fep_{name}.json').read_text())['learner']
        model = learner['gradient_booster']['model']
        limit = (int(learner['attributes']['best_iteration']) + 1) * 5
        trees = []
        for t, group in zip(model['trees'][:limit], model['tree_info'][:limit]):
            assert not any(t['split_type']), 'Categorical trees are unsupported'
            trees.append(dict(left=t['left_children'], right=t['right_children'],
                feature=t['split_indices'], value=t['split_conditions'], missingLeft=t['default_left'], group=group))
        base = json.loads(learner['learner_model_param']['base_score'])
        if not isinstance(base, list): base = [base] * 5
        result['horizons'].append(dict(frames=frames, baseline=base, trees=trees,
            projection=np.linalg.matrix_power(fep.transition, frames).ravel().tolist()))
    destination = ROOT / 'CombatGame/Assets/Resources/FepSupervisedModel.json'
    destination.write_text(json.dumps(result, separators=(',', ':')))
    print(destination)
    return result

if __name__ == '__main__': export()
