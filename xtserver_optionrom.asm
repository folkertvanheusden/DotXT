	org 0
	db $55
	db $aa
	db 1

; trigger program load
	mov dx,#$f001
	mov al,#$ff
	out dx,al

; set interrupts
	mov si,#($60 * 4)
	mov [si + 0],#screenshot
	mov [si + 2],#$d000

; start program
	jmp far $1000:0000

ax_storage:
	dw 0
dx_storage:
	dw 0

screenshot:
	mov [ax_storage], ax
	mov [dx_storage], dx
	mov dx,#$f001
	mov al,#$01
	out dx,al
	mov dx,[dx_storage]
	mov ax,[ax_storage]
	iret
