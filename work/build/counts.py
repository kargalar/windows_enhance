import gzip
p='src/WinForge.Modules.Extras/Resources/TurkishNgramModel.tsv.gz'; target={'sarkilar','şarkılar','olum','ölüm','benım','benim','askim','aşkım','askım'}
with gzip.open(p,'rt',encoding='utf-8') as f:
 for line in f:
  a=line.rstrip('\n').split('\t')
  if len(a)==3 and a[0]=='u' and a[1] in target: print(a[1],a[2])
