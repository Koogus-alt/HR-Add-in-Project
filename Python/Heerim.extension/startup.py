# -*- coding: utf-8 -*-
import socket
import threading
import sys
import os
import datetime
from Autodesk.Revit.UI import IExternalEventHandler, ExternalEvent
from pyrevit import DB, UI

# LOGGING SETUP
LOG_FILE = r"C:\Users\thomashj\.gemini\antigravity\playground\solitary-omega\server_log.txt"

def log(msg):
    try:
        timestamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        with open(LOG_FILE, "a") as f:
            f.write("[{}] {}\n".format(timestamp, msg))
    except:
        pass

log("Startup script initiated.")

class ExecutionHandler(IExternalEventHandler):
    def __init__(self):
        self.code = None
        self.result = None
        self.error = None
        self.completed_event = threading.Event()

    def Execute(self, uiapp):
        log("Execute event triggering...")
        try:
            uidoc = uiapp.ActiveUIDocument
            if not uidoc:
                self.error = "No active document."
                log("Error: No active document")
                return

            doc = uidoc.Document
            
            exec_globals = {
                '__builtins__': __builtins__,
                'doc': doc,
                'uidoc': uidoc,
                'uiapp': uiapp,
                'DB': DB,
                'UI': UI,
                'Transaction': DB.Transaction
            }
            
            log("Executing code...")
            exec(self.code, exec_globals)
            self.result = "Success"
            log("Execution success.")
        except Exception as e:
            import traceback
            self.error = traceback.format_exc()
            log("Execution Error: " + str(e))
        finally:
            self.completed_event.set()

    def GetName(self):
        return "Heerim Remote Execution Handler"

def server_thread(handler, event):
    HOST = '127.0.0.1'
    PORT = 54321
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    
    try:
        log("Attempting to bind to {}:{}".format(HOST, PORT))
        s.bind((HOST, PORT))
        s.listen(1)
        log("Server listening...")
        
        while True:
            log("Waiting for connection...")
            conn, addr = s.accept()
            try:
                log("Connection from: " + str(addr))
                data = b""
                while True:
                    chunk = conn.recv(4096)
                    if not chunk: break
                    data += chunk
                    if len(chunk) < 4096: break
                
                if not data:
                    continue

                code = data.decode('utf-8')
                log("Received code (len={})".format(len(code)))
                
                handler.code = code
                handler.error = None
                handler.result = None
                handler.completed_event.clear()
                
                log("Raising ExternalEvent")
                event.Raise()
                
                log("Waiting for main thread...")
                handler.completed_event.wait()
                
                if handler.error:
                    response = "Error: " + handler.error
                else:
                    response = "OK"
                
                log("Sending response: " + response[:50] + "...")
                conn.sendall(response.encode('utf-8'))
                
            except Exception as e:
                log("Connection handling error: " + str(e))
            finally:
                conn.close()
                
    except Exception as e:
        log("Server Crash: " + str(e))
    finally:
        s.close()
        log("Server closed.")

try:
    log("Creating handler and event...")
    handler = ExecutionHandler()
    event = ExternalEvent.Create(handler)
    
    log("Starting thread...")
    t = threading.Thread(target=server_thread, args=(handler, event))
    t.daemon = True
    t.start()
    log("Thread started.")
except Exception as e:
    log("Startup Error: " + str(e))
