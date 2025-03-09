	org 0
	db $55
	db $aa
	db 1

; trigger program load
	mov dx,#$f001
	mov al,#$ff
	out dx,al

; set interrupts
	xor ax,ax
	mov ds,ax

	mov si,#($60 * 4)
	mov cx,#$0b
set_loop:
	mov [si + 0],#do_nothing
	mov [si + 2],#$d000
	add si, #$04
	loopnz set_loop

	mov si,#($60 * 4)
	mov [si + 0],#screenshot
	mov [si + 2],#$d000

	mov si,#($63 * 4)
	mov [si + 0],#send_ax
	mov [si + 2],#$d000

	mov si,#($64 * 4)
	mov [si + 0],#process_64
	mov [si + 2],#$d000

	mov si,#($65 * 4)
	mov [si + 0],#process_65
	mov [si + 2],#$d000

; start program
	jmp far $1000:0000

ax_storage:
	dw 0
dx_storage:
	dw 0

do_nothing:
	iret

screenshot:
	mov [ax_storage], ax
	mov [dx_storage], dx
	mov dx,#$f001
	mov ax,#$6001
	out dx,ax
	mov dx,[dx_storage]
	mov ax,[ax_storage]
	iret

send_ax:
	mov [dx_storage], dx
	mov dx,#$f063
	out dx,ax
	mov dx,[dx_storage]
	iret

process_64:
	mov [dx_storage], dx
	mov dx,#$f064
send_64:
	mov al,[si]
	inc si
	out dx,al
	dec cx
	loopnz send_64
	mov dx,[dx_storage]
	iret

process_65:
	mov [dx_storage], dx
	mov dx,#$f065
	out dx,al
	mov dx,[dx_storage]
	iret
