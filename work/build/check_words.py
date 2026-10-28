import gzip
p='src/WinForge.Modules.Extras/Resources/TurkishNgramModel.tsv.gz'; want={'calistigi','şarkılar','sarkilar','calistigini','yapildigini','yapıldığını'}
with gzip.open(p,'rt',encoding='utf-8') as f:
 for line in f:
  a=line.rstrip('\n').split('\t')
  if len(a)==3 and a[0]=='u' and a[1] in want: print(a[1],a[2])
