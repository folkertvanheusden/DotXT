	org $0800

; documentation used: https://wiki.osdev.org/Floppy_Disk_Controller

	FDC_CMD_READ_TRACK	=	2
	FDC_CMD_SPECIFY		=	3
	FDC_CMD_CHECK_STAT	=	4
	FDC_CMD_WRITE_SECT	=	5
	FDC_CMD_READ_SECT	=	6
	FDC_CMD_CALIBRATE	=	7
	FDC_CMD_CHECK_INT	=	8
	FDC_CMD_WRITE_DEL_S	=	9
	FDC_CMD_READ_ID_S	=	0xa
	FDC_CMD_READ_DEL_S	=	0xc
	FDC_CMD_FORMAT_TRACK	=	0xd
	FDC_CMD_SEEK		=	0xf

	FDC_CMD_EXT_SKIP	=	0x20
	FDC_CMD_EXT_DENSITY	=	0x40
	FDC_CMD_EXT_MULTITRACK	=	0x80

	FLPYDSK_SECTOR_DTL_128	=	0
	FLPYDSK_SECTOR_DTL_256	=	1
	FLPYDSK_SECTOR_DTL_512	=	2
	FLPYDSK_SECTOR_DTL_1024	=	4

	FLPYDSK_GAP3_LENGTH_STD = 42
	FLPYDSK_GAP3_LENGTH_5_14= 32
	FLPYDSK_GAP3_LENGTH_3_5 = 27

	cli
	cld

	xor ax,ax
	mov si,ax

	out $80,al

	; initialize stack
	mov ss,ax
	mov ax,#$0800
	mov sp,ax

	; set interrupt vector for floppy
	mov ax,#intvec
	mov word [6 * 4 + 0],ax
	mov ax,cs
	mov word [6 * 4 + 2],ax
	; skip over interrupt vector & data
	jmp near init_continue
interrupt_triggered:
	db $00
intvec:
	push ax
	mov al,#$ff
	mov interrupt_triggered,al
	;
	mov al,#$20  ; EOI code
	out $20,al  ; send 'end of interrupt'
	pop ax
	iret

progress:
	dw $0000

unmask_floppy:
	; * PUT OCW1
	push AX
	; 8259 port 21
	mov al,#$bf
	; IMR, interrupt mask register
	; only allow irq 6
	out $21,al
	pop AX
	ret

mask_floppy:
	; * PUT OCW1
	push AX
	; 8259 port 21
	mov al,#$ff
	; IMR, interrupt mask register
	; allow no interrupts
	out $21,al
	pop AX
	ret

clear_interrupt_flag:
	push ax
	xor ax,ax
	mov interrupt_triggered,al
	pop ax
	ret

wait_interrupt_success:
	push cx
	mov cx,#0000
loop_02:
	cmp byte interrupt_triggered,#$ff
	jz int_received
	loop loop_02
	cli
	hlt
int_received:
	pop cx
	ret

wait_interrupt_none:
	push cx
	mov cx,#0000
loop_win:
	cmp byte interrupt_triggered,#$ff
	jz win_int_received2
	loop loop_win
	jp loop_win_ok
win_int_received2:
	cli
	hlt ; error!
loop_win_ok:
	pop cx
	ret

reset_floppy:
	; trigger floppy reset
	mov dx,#$3f2
	; ...enter
	mov al,#$00
	out dx,al
	; ...exit
	mov al,#$0c
	out dx,al
	ret

enable_motor0:
	mov dx,#$3f2
	mov al,#$10
	out dx,al
	ret

wait_motor0_spinup:
	; sleep around 250 ms
	mov cx,#0000
wait_motor0_spinup_loop:
	loop wait_motor0_spinup_loop
	; anything else?
	ret

setup_dma_1_sector_read:
	; https://wiki.osdev.org/ISA_DMA#Floppy_Disk_DMA_Initialization
	; set DMA channel 2 to transfer data from BX - BX + 0x0200 in memory
	; set the counter to 0x200, the length of a sector, assuming 512 byte sectors
	; transfer length = counter + 1
	mov al,#$06
	out 0x0a, al      ; mask DMA channel 2 and 0 (assuming 0 is already masked)

	mov al,#$ff
	out 0x0c, al      ; reset the master flip-flop

	mov al,bl
	out 0x04, al      ; address low byte
	mov al,bh
	out 0x04, al      ; address high byte

	mov al,#$ff
	out 0x0c, al      ; reset the master flip-flop (again!!!)

	mov al,#$00
	out 0x05, al      ; count to 0x0200 (low byte)
	mov al,#$02
	out 0x05, al      ; count to 0x0200 (high byte),

	mov al,#$00
	out 0x81, al      ; external page register to 0 for total address of 00 10 00

	mov al,#$02
	out 0x0a, al      ; unmask DMA channel 2
	ret

set_floppy_read:
	mov al,#$06
	out 0x0a, al      ; mask DMA channel 2 and 0 (assuming 0 is already masked)

	mov al,#$56
	out 0x0b, al      ; 01010110 single transfer, address increment, autoinit, read, channel2)

	mov al,#$02
	out 0x0a, al      ; unmask DMA channel 2
	ret

	; AH = cmd
send_floppy_cmd:
	mov cx,#500
	mov dx,#$3f4
send_floppy_cmd_loop:
	in al,dx
	and al,#128
	test al,al
	jnz send_floppy_cmd_ok
	loop send_floppy_cmd_loop
	hlt
send_floppy_cmd_ok:
	mov dx,#$3f5
	mov al,ah
	out dx,al
	ret

; *** MAIN ***
init_continue:
	; * test reset floppycontroller *
	mov progress,#$0001

	call unmask_floppy
	call clear_interrupt_flag

	mov progress,#$0002
	sti
	mov progress,#$0003
	call reset_floppy
	call wait_interrupt_success

	; * test switch on motor *
	mov progress,#$0101
	call enable_motor0
	mov progress,#$0102
	call wait_motor0_spinup
	mov progress,#$0103

	; * read sector *
	; dma setup
	mov progress,#$0201
	call setup_dma_1_sector_read
	mov progress,#$0202
	call set_floppy_read
	mov progress,#$0203
	;
	call clear_interrupt_flag
	; floppy controller setup
	mov ah, #(FDC_CMD_READ_SECT | FDC_CMD_EXT_MULTITRACK | FDC_CMD_EXT_SKIP | FDC_CMD_EXT_DENSITY)
	call send_floppy_cmd
	mov ah, #$00 ; head * 4 + current_drive
	call send_floppy_cmd
	mov ah, #$00 ; track
	call send_floppy_cmd
	mov ah, #$00 ; head
	call send_floppy_cmd
	mov ah, #$01 ; sector
	call send_floppy_cmd
	mov ah, #FLPYDSK_SECTOR_DTL_512
	call send_floppy_cmd
	mov ah, #$02 ; stop at sector(?)
	call send_floppy_cmd
	mov ah, #FLPYDSK_GAP3_LENGTH_3_5
	call send_floppy_cmd
	mov ah, #$ff
	call send_floppy_cmd
	;
	call wait_interrupt_success

// TODO

	; *** all good ***
	cli
	mov progress,#$7fff
	mov ax,#$a5ee
	mov si,ax
	mov al,#$ff
	out $80,al
	hlt
