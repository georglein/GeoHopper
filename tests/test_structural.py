"""Regression tests for structural Output identity, units, arrays and atomic failure.

Independent protocol doubles are not native FE acceptance. Field names follow the
2025.1 reference and 2024.1 compatibility notes; force units follow Reference 9.4.9.
"""
import json
import tempfile
import threading
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import encryption
import test_analysis as fixture

INFO, PHASE, SOURCE, PART1, PART2 = [str(uuid.uuid4()) for _ in range(5)]
commands = []
result_lists = {}

class Output(BaseHTTPRequestHandler):
    """Separate Output identities, including a source group with two cut parts."""
    def log_message(self, *_):
        """Never log traffic or credentials."""
        pass

    def do_POST(self):
        """Return metadata/list/array envelopes and inject controlled failures."""
        raw=json.loads(self.rfile.read(int(self.headers['Content-Length'])))
        req=json.loads(encryption.decrypt(raw['RequestData'],raw['Code'],fixture.PASSWORD))
        action=req['action']; state=fixture.state
        anchor=state.get('anchor',False)
        source='GH_Anchor_Strut1' if anchor else 'GH_Plate_Left'
        if self.path=='/namedobjects':
            name=action['namedobjects'][0]
            mapping={'GeneralInfo':INFO,'GH_Phase_Deepen':PHASE,source:SOURCE}
            response={'namedobjects':{name:{'success':name in mapping and not (state.get('missing_source') and name==source),
                       'returnedobject':{'guid':mapping.get(name,SOURCE),'type':name}}}}
        elif self.path=='/propertyvalues':
            q=action['propertyvalues']; owner=q['owner']; prop=q['propertyname']
            names={PHASE:'GH_Phase_Deepen',SOURCE:source,PART1:source+'_1',PART2:source+'_2'}
            values={'Title':'GeoHopper:AnalysisDemo','Filename':state['file'],'UnitLength':'m','UnitForce':'N','Name':names.get(owner,'GeneralInfo')}
            if state.get('wrong_file') or (state.get('switch_output') and len(commands)>1): values['Filename']+='.other'
            if state.get('wrong_force'): values['UnitForce']='kN'
            if state.get('wrong_length'): values['UnitLength']='mm'
            if state.get('wrong_phase') and owner==PHASE: values['Name']='GH_Phase_Initial'
            if state.get('wrong_source') and owner==SOURCE: values['Name']='Other'
            response={'queries':{owner:{'properties':{prop:{'value':values[prop]}}}}}
        elif self.path=='/list':
            query=action['listqueries'][0]
            if query['guid'] in result_lists:
                values=result_lists[query['guid']]
                if query['method']=='count': data=200001 if state.get('oversized_list') else len(values)
                else:
                    data=values[query['startindex']:query['stopindex']]
                    if state.get('short_slice'): data=data[:-1]
                    if state.get('json_items'): data=[{'type':'JSON','json':v} for v in data]
                response={'listqueries':[{'success':not state.get('failed_list'),'methodname':query['method'],'outputdata':data}]}
                self.reply(req,response)
                return
            assert query['guid']==SOURCE
            count=1 if anchor else 2
            if state.get('empty_parts'): count=0
            if query['method']=='count': data=count
            else:
                assert query['startindex']==0 and query['stopindex']==count
                data=[{'guid':part,'type':'NodeToNodeAnchor' if anchor else 'Plate','islistable':False} for part in ([PART1] if anchor else [PART1,PART2])]
                if state.get('duplicate_part'): data[1]=data[0]
            response={'listqueries':[{'success':True,'methodname':query['method'],'outputdata':data}]}
        elif self.path=='/commands':
            cmd,=action['commands']; commands.append(cmd); tokens=cmd.split()
            assert tokens[0]=='getresults' and tokens[2]==PHASE and tokens[-1]=='"node"',cmd
            assert tokens[1] in (PART1,PART2), 'Must query cut elements, not source groups'
            field=tokens[3].split('.')[-1]
            if anchor:
                assert tokens[3].startswith('ResultTypes.NodeToNodeAnchor.')
                arrays={'X':[-1,1],'Y':[-.5,-.5],'N':[-9000,-9000]}
            else:
                assert tokens[3].startswith('ResultTypes.Plate.')
                y=[-2,-4,-2] if tokens[1]==PART1 else [0,-2]
                arrays={'X':[-1]*len(y),'Y':y,'Ux':[.01]*len(y),'Uy':[-.02]*len(y),'Nx2D':[-100]*len(y),'Q2D':[200]*len(y),'M2D':[-300]*len(y)}
            data=arrays[field]
            if state.get('foreign_coordinates') and field=='X': data=[5]*len(data)
            if state.get('mismatch') and field=='Uy': data=data[:-1]
            if state.get('late_failure') and tokens[1]==PART2 and field=='M2D': data=['not found']*len(data)
            feedback={'success':not state.get('native_error'),'extrainfo':'Controlled structural failure','returnedobjects':[{'type':'JSON','json':data}]}
            if state.get('list_results'):
                handle=str(uuid.uuid4()); result_lists[handle]=data
                feedback['returnedobjects']=[{'type':'ResultArray','guid':handle,'islistable':True}]
            response={'commands':[{'feedback':feedback}]}
        else: raise AssertionError(self.path)
        self.reply(req,response)

    def reply(self,req,response):
        """Send one authenticated response without logging numerical payloads."""
        response['ReplyCode']=req['ReplyCode']
        data,iv=encryption.encrypt(json.dumps(response),fixture.PASSWORD)
        body=json.dumps({'Response':data,'Code':iv}).encode()
        self.send_response(200); self.send_header('Content-Length',str(len(body))); self.end_headers(); self.wfile.write(body)

def run(inp,out,folder,**options):
    """Perform a full production build, solve and structural read against the doubles."""
    commands.clear(); result_lists.clear()
    mode=options.pop('mode','structural-anchor' if options.get('anchor') else 'structural-plate')
    return fixture.run(inp.server_port,folder,mode=mode,output_port=out.server_port,**options)

if __name__=='__main__':
    inp=ThreadingHTTPServer(('127.0.0.1',0),fixture.Handler)
    out=ThreadingHTTPServer(('127.0.0.1',0),Output)
    for server in (inp,out): threading.Thread(target=server.serve_forever,daemon=True).start()
    check=fixture.check
    try:
        with tempfile.TemporaryDirectory(prefix='GeoHopper-structures-') as folder:
            r=run(inp,out,folder)
            check('Plate cut parts return all rows',r['Success'] and len(r['Rows'])==5)
            check('Plate rows sorted with duplicates retained',[x['Y'] for x in r['Rows']]==[-4,-2,-2,-2,0])
            check('Plate displacement and local forces retain native signs',all(x['Ux']==.01 and x['Uy']==-.02 and x['N']==-100 and x['Q']==200 and x['M']==-300 for x in r['Rows']))
            check('Every result field queried for each native part',len(commands)==14)
            r=run(inp,out,folder,anchor=True)
            check('Modern anchor N is read per individual anchor',r['Success'] and len(r['Rows'])==2 and all(x['N']==-9000 for x in r['Rows']))
            check('Anchor reader uses modern result name',any('ResultTypes.NodeToNodeAnchor.N ' in c for c in commands) and len(commands)==3)
            for option in ('wrong_force','wrong_length','wrong_phase','wrong_source','missing_source','empty_parts','duplicate_part'):
                r=run(inp,out,folder,**{option:True})
                check(option+' blocks all numerical queries',not r['Success'] and not r['Rows'] and not commands)
            for option in ('foreign_coordinates','mismatch','late_failure','native_error','switch_output'):
                r=run(inp,out,folder,**{option:True})
                check(option+' discards partial structural rows',not r['Success'] and not r['Rows'])
            for anchor in (False,True):
                r=run(inp,out,folder,list_results=True,anchor=anchor)
                check('Native list handles read for '+('anchors' if anchor else 'plates'),r['Success'] and len(r['Rows'])==(2 if anchor else 5))
            r=run(inp,out,folder,list_results=True,json_items=True)
            check('JSON scalar items inside native lists supported',r['Success'])
            for option in ('short_slice','failed_list','oversized_list','late_failure'):
                r=run(inp,out,folder,list_results=True,**{option:True})
                check(option+' rejects native list without partial results',not r['Success'] and not r['Rows'])
            r=run(inp,out,folder,mode='structural-stale')
            check('Recalculation invalidates structural receipt',not r['Success'] and not commands and 'stale' in r['Error'])
    finally:
        for server in (inp,out): server.shutdown(); server.server_close()
