/* WASI libc has no system() or tmpfile(); PUC's os.execute / io.tmpfile reference them. Stubs that always fail.
   The bench programs only print, so neither is ever called. */
#include <stdio.h>
#include <stdlib.h>
#include <errno.h>

int system(const char *cmd) { (void)cmd; errno = ENOSYS; return -1; }
FILE *tmpfile(void) { errno = ENOSYS; return NULL; }
