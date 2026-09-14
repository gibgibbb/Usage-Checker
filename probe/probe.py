"""Bounded, credential-redacting Codex compatibility probe. No existing turns mutated."""
import argparse, hashlib, json, os, pathlib, queue, subprocess, threading, time

ROOT = pathlib.Path(__file__).resolve().parents[1]

class Client:
    def __init__(self, binary, extra=None):
        self.p = subprocess.Popen([binary, *(extra or []), 'app-server', '--listen', 'stdio://'],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            text=True, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW)
        self.q = queue.Queue(); self.events = []; self.n = 0
        threading.Thread(target=self.read, daemon=True).start()
        threading.Thread(target=lambda: list(self.p.stderr), daemon=True).start()
        self.call('initialize', {'clientInfo': {'name':'codex_usage_probe','version':'0.1.0'},
                    'capabilities':{'experimentalApi':True}})
        self.send({'method':'initialized'})
    def read(self):
        for line in self.p.stdout:
            try: self.q.put(json.loads(line))
            except ValueError: pass
        self.q.put({'closed':True})
    def send(self, message):
        self.p.stdin.write(json.dumps(message)+'\n'); self.p.stdin.flush()
    def call(self, method, params=None, timeout=45):
        self.n += 1; ident=self.n
        self.send({'id':ident,'method':method,'params':params or {}})
        end=time.monotonic()+timeout
        while time.monotonic()<end:
            m=self.q.get(timeout=max(.01,end-time.monotonic()))
            if m.get('closed'): raise RuntimeError('transport closed')
            if m.get('id')==ident and 'method' not in m:
                if 'error' in m: raise RuntimeError(json.dumps(m['error']))
                return m.get('result')
            self.events.append(m)
        raise TimeoutError(method)
    def wait_turn(self, turn, seconds=60):
        end=time.monotonic()+seconds
        while time.monotonic()<end:
            for m in self.events:
                if m.get('method')=='turn/completed' and m.get('params',{}).get('turn',{}).get('id')==turn:
                    return m['params']['turn']['status']
            m=self.q.get(timeout=max(.01,end-time.monotonic())); self.events.append(m)
        raise TimeoutError('turn/completed')
    def close(self):
        self.p.stdin.close()
        try: self.p.wait(timeout=5)
        except subprocess.TimeoutExpired: self.p.kill(); self.p.wait()

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--binary',required=True); ap.add_argument('--exercise',action='store_true')
    args=ap.parse_args(); report={'at':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime())}
    c=Client(args.binary)
    try:
        account=c.call('account/read',{'refreshToken':False}).get('account') or {}
        report['account']={k:account.get(k) for k in ('type','planType')}
        limits=c.call('account/rateLimits/read'); report['limits']=limits
        loaded=c.call('thread/loaded/list'); report['independent_loaded_threads']=loaded
        try: report['hooks']=c.call('hooks/list',{'cwds':[str(ROOT)]})
        except Exception as e: report['hooks_error']=str(e)
        if args.exercise:
            owned=c.call('thread/start',{'cwd':str(ROOT/'probe'), 'ephemeral':True,
                'approvalPolicy':'never','sandbox':'read-only'})['thread']['id']
            turn=c.call('turn/start',{'threadId':owned,'input':[{'type':'text','text':'Reply with exactly PROBE_OK. Do not call any tools.'}]})['turn']['id']
            report['completion']=c.wait_turn(turn)
            turn=c.call('turn/start',{'threadId':owned,'input':[{'type':'text','text':'Write a long explanation of sorting algorithms. Do not use tools.'}]})['turn']['id']
            c.call('turn/interrupt',{'threadId':owned,'turnId':turn})
            report['cancellation']=c.wait_turn(turn)
            try:
                c.call('turn/start',{'threadId':'not-a-thread','input':[{'type':'text','text':'probe'}]})
            except Exception as e: report['invalid_request_failure']=str(e)
            # A terminal model failure is different from an RPC validation failure.
            bad=c.call('thread/start',{'cwd':str(ROOT/'probe'),'ephemeral':True,
                'approvalPolicy':'never','sandbox':'read-only','model':'codex-monitor-invalid-model-probe'})['thread']['id']
            try:
                turn=c.call('turn/start',{'threadId':bad,'input':[{'type':'text','text':'Probe.'}]})['turn']['id']
                report['terminal_failure']=c.wait_turn(turn,30)
            except Exception as e: report['terminal_failure_error']=str(e)
            approval=c.call('thread/start',{'cwd':str(ROOT/'probe'),'ephemeral':True,
                'approvalPolicy':'untrusted','approvalsReviewer':'user','sandbox':'read-only'})['thread']['id']
            turn=c.call('turn/start',{'threadId':approval,'input':[{'type':'text','text':
                'Compatibility test: use the shell tool to run Get-Date exactly once. Do not edit files or run other commands.'}]})['turn']['id']
            end=time.monotonic()+40; observed=None
            while time.monotonic()<end:
                for m in c.events:
                    if 'requestApproval' in m.get('method','') and m.get('params',{}).get('threadId')==approval:
                        observed=m['method']; break
                if observed: break
                try: c.events.append(c.q.get(timeout=1))
                except queue.Empty: pass
            report['approval_request']=observed or 'not observed within 40 seconds'
            report['approval_wait_flags']=[m.get('params',{}).get('status') for m in c.events
                if m.get('method')=='thread/status/changed' and m.get('params',{}).get('threadId')==approval]
            c.call('turn/interrupt',{'threadId':approval,'turnId':turn})
            report['approval_cancelled']=c.wait_turn(turn,10)
        report['event_methods']=sorted({m['method'] for m in c.events if 'method' in m})
    finally: c.close()
    # Account identifier used only as a fingerprint, never copy credentials.
    if report.get('limits',{}).get('accountId'):
        report['account_fingerprint']=hashlib.sha256(report['limits'].pop('accountId').encode()).hexdigest()[:16]
    out=ROOT/'probe'/'results'; out.mkdir(exist_ok=True)
    name='exercise' if args.exercise else 'independent'
    (out/(name+'.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    # Hook listings can contain local command configuration: don't print them.
    print(json.dumps({k:v for k,v in report.items() if k!='hooks'},indent=2))

if __name__=='__main__': main()
