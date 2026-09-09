"""Parity of exported early-stopped trees against original XGBoost, including NaNs."""
import json
from pathlib import Path
import numpy as np
from xgboost import XGBClassifier
ROOT=Path(__file__).resolve().parents[2]
model=json.loads((ROOT/'CombatGame/Assets/Resources/FepSupervisedModel.json').read_text())
rng=np.random.default_rng(47)
values=rng.normal(size=(128,61)).astype(np.float32)
values[rng.random(values.shape)<.08]=np.nan
cases=[]
for h,name in zip(model['horizons'],['50ms','100ms','200ms']):
    original=XGBClassifier();original.load_model(ROOT/f'ML/artifacts/hybrid_experiments/models/xgboost_fep_{name}.json')
    expected=original.predict(values,output_margin=True)
    actual=np.tile(np.asarray(h['baseline'],dtype=np.float32),(len(values),1))
    for t in h['trees']:
        for i,row in enumerate(values):
            node=0
            while t['left'][node]>=0:
                v=row[t['feature'][node]]
                left=t['missingLeft'][node] if np.isnan(v) else v<t['value'][node]
                node=t['left'][node] if left else t['right'][node]
            actual[i,t['group']]+=t['value'][node]
    np.testing.assert_allclose(actual,expected,atol=2e-5,rtol=2e-5)
    cases.append({'frames':h['frames'],'expected':expected.tolist()})
    print(name,'128 rows logits match, max error',float(np.max(np.abs(actual-expected))))
Path(__file__).with_name('parity_cases.json').write_text(json.dumps({'values':[[None if np.isnan(v) else float(v) for v in row] for row in values],'cases':cases}))
