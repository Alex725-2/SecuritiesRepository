import urllib.request,urllib.error,json
for path,body in [('/api/dev/token',{'role':'Admin'}),('/swagger/v1/swagger.json',None)]:
 req=urllib.request.Request('http://127.0.0.1:5089'+path,data=json.dumps(body).encode() if body else None,headers={'Content-Type':'application/json'})
 try:
  response=urllib.request.urlopen(req);status=response.status
 except urllib.error.HTTPError as e:status=e.code
 print(path,status);assert status==404
