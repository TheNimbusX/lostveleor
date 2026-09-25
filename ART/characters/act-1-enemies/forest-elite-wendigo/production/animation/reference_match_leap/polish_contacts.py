"""Малые поправки опор между поставленными позами; движение и тайминг сохраняются."""
from fit_leap import *
samples=np.array(json.loads((HERE/'animation_samples.json').read_text()))
result=[];last_delta=np.zeros(samples.shape[1]);audit=[]
for f,original in enumerate(samples):
 mats=fk(original)@inverses
 xyz=np.einsum('vb,bij,vj->vi',weights,mats,verts)[:,:3]
 critical=np.unique(np.r_[floor_ix,np.where(xyz[:,2]<.08)[0]])
 sv=verts[critical];sw=weights[critical];old=deform(original)
 def error(v):
  ma=fk(v)@inverses
  z=np.einsum('vb,bj,vj->v',sw,ma[:,2,:],sv)
  markers=deform(v)
  return np.r_[(v-original)*np.r_[[150,150,150],np.repeat(20,len(v)-3)],np.minimum(z-.002,0)*600,((markers-old)/.03).ravel(),(v-original-last_delta)*5]
 if xyz[:,2].min()<.001:
  solve=least_squares(error,original,max_nfev=32,ftol=1e-6,xtol=1e-6);x=solve.x
 else:x=original.copy()
 last_delta=x-original;result.append(x.tolist())
 final=np.einsum('vb,bij,vj->vi',weights,fk(x)@inverses,verts)[:,:3]
 audit.append({'frame':f/4,'before_min_z':float(xyz[:,2].min()),'after_min_z':float(final[:,2].min()),'max_rotation_change_degrees':float(np.linalg.norm((x-original)[3:].reshape(-1,3),axis=1).max()*180/np.pi)})
(HERE/'polished_samples.json').write_text(json.dumps(result))
(HERE/'contact_polish_audit.json').write_text(json.dumps(audit,indent=2))
print('worst floor',min(a['after_min_z'] for a in audit),'max rotation correction',max(a['max_rotation_change_degrees'] for a in audit))
