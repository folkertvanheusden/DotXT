// do not run this shortly before midnight (local time)

// with code from https://wiki.osdev.org/Floppy_Disk_Controller
// and https://www.ardent-tool.com/floppy/Floppy_Programming.html

#include <bios.h>
#include <conio.h>
#include <i86.h>
#include <stdio.h>

#define byte unsigned char

byte buffer[18 * 512];  // max HD floppy

enum FloppyRegisters
{
	STATUS_REGISTER_A                = 0x3F0, // read-only
	STATUS_REGISTER_B                = 0x3F1, // read-only
	DIGITAL_OUTPUT_REGISTER          = 0x3F2,
	TAPE_DRIVE_REGISTER              = 0x3F3,
	MAIN_STATUS_REGISTER             = 0x3F4, // read-only
	DATARATE_SELECT_REGISTER         = 0x3F4, // write-only
	DATA_FIFO                        = 0x3F5,
	DIGITAL_INPUT_REGISTER           = 0x3F7, // read-only
	CONFIGURATION_CONTROL_REGISTER   = 0x3F7  // write-only
};

enum FloppyCommands
{
	READ_TRACK =                 2,	// generates IRQ6
	SPECIFY =                    3,      // * set drive parameters
	SENSE_DRIVE_STATUS =         4,
	WRITE_DATA =                 5,      // * write to the disk
	READ_DATA =                  6,      // * read from the disk
	RECALIBRATE =                7,      // * seek to cylinder 0
	SENSE_INTERRUPT =            8,      // * ack IRQ6, get status of last command
	WRITE_DELETED_DATA =         9,
	READ_ID =                    10,	// generates IRQ6
	READ_DELETED_DATA =          12,
	FORMAT_TRACK =               13,     // *
	DUMPREG =                    14,
	SEEK =                       15,     // * seek both heads to cylinder X
	VERSION =                    16,	// * used during initialization, once
	SCAN_EQUAL =                 17,
	PERPENDICULAR_MODE =         18,	// * used during initialization, once, maybe
	CONFIGURE =                  19,     // * set controller parameters
	LOCK =                       20,     // * protect controller params from a reset
	VERIFY =                     22,
	SCAN_LOW_OR_EQUAL =          25,
	SCAN_HIGH_OR_EQUAL =         29
};

void dump(byte *p, int n)
{
	int i;
	for(i=0; i<n; i++)
		printf(" %02x", p[i]);
	printf("\n");
}

long get_ticks()
{
	long time_of_day;
	_bios_timeofday(_TIME_GETCLOCK, &time_of_day);
	return time_of_day;
}

volatile byte received_irq = 0;
void interrupt floppy_handler()  // FIXME int 6
{
	received_irq = 1;

	outp(0x20, 0x20);  /* Non-specific EOI to master PIC */
}

void switch_on()
{
	long s = get_ticks();  /* wait a second */

	outp(DIGITAL_OUTPUT_REGISTER, 0x1c);  /* 0x10: motor 0 on, 0x04: set (instead of reset), 0x08: dma */

	while(get_ticks() - s < 18) {
	}
}

void switch_off()
{
	long s = get_ticks();  /* wait a second */

	outp(DIGITAL_OUTPUT_REGISTER, 0x18);  /* 0x10: motor 0 on, 0x00: reset (instead of 0x04), 0x08: dma */

	while(get_ticks() - s < 18) {
	}
}

void start_dma()
{
	unsigned segment = FP_SEG(buffer);
	unsigned offset  = FP_OFF(buffer);
	long addr = (long)segment * 16 + offset;
	byte page = addr >> 16;

	outp(0x08, 0x14);  // init & disable dma controller

	outp(0x0b, 0x46);  // single write transfer to main mem, channel 2

	// output the address to the DMA controller
	outp(0x0c, 255);  // reset flip-flop
	outp(0x04, addr & 255);  // low byte
	outp(0x04, addr >> 8);  // high byte

	outp(0x0c, 255);  // reset flipflop
	outp(0x05, 0xff);  // 511 (sector size - 1)
	outp(0x05, 0x01);

	outp(0x81, page);  // page register

	outp(0x0a, 0x02);  // release channel 2

	outp(0x08, 0x10);  // enable dma
}

int send_command(byte cmd[], int send_n, byte wait_reply, byte result[], int recv_n)
{
	int i;
	long start;
	unsigned dummy;
	int msr = inp(MAIN_STATUS_REGISTER);
	if ((msr & 0xc0) != 0x80) {
		printf("floppy not ready for command\n");
		return -1;
	}

	printf("CMD: ");
	for(i=0; i<send_n; i++)
		printf("%02x ", cmd[i]);
	printf("\n");

	received_irq = 0;

	if (wait_reply == 2)
		start_dma();

	for(i=0; i<send_n; i++)
		outp(DATA_FIFO, cmd[i]);

	if (wait_reply == 0)
		return 0;

	start = get_ticks();
	while(received_irq == 0) {
		if (get_ticks() - start >= 18) {  // 1 second
			printf("no irq from floppy\n");
			return -1;
		}
	}

	if (wait_reply == 1)
		return 0;

	start = get_ticks();
	while((inp(MAIN_STATUS_REGISTER) & 0xc0) != 0xc0) {
		if (get_ticks() - start >= 18) {  // 1 second
			printf("no response from floppy\n");
			return -1;
		}
	}

	for(i=0; i<recv_n; i++) {
		if ((inp(MAIN_STATUS_REGISTER) & 0xd0) != 0xd0) {
			printf("msr invalid\n");
			return -1;
		}

		result[i] = inp(DATA_FIFO);
	}

	dummy = inp(MAIN_STATUS_REGISTER);
	if ((dummy & 0x80) != 0x80) {
		outp(0xffff, 4);
		printf("resulting msr invalid: %02x\n", dummy);
		return -1;
	}

	return 0;
}

void enter()
{
	printf("\aPress enter: ");  // beep
	getchar();
}

void main()
{
	int i;
	byte cmd_read[] = { 0x06 /* read */, 0x00 /* head/unit 0 */, 0/*c*/, 0/*h*/, 1/*r*/, 2/*n*/, 9/*eot*/, 42/*gpl*/, 255/*dtl*/ };
	byte cmd_specify[] = { 0x04 /*specify*/, 0xff/*SPRT/HUT: max delay*/, 254/*loadtimemax + dma */ };
	byte cmd_seek[] = { 0x0f /*seek*/, 0x00/*head/unit*/, 0/*cylinder*/ };
	byte result[7] = { 0 };
	typedef void interrupt (*inthandler)();
	inthandler far *ivt = (inthandler far *)0;
	inthandler oldhandler;

	printf(__DATE__ " " __TIME__ "\n");

	// INIT
	oldhandler = ivt[0x0e];  // Save old handler, https://wiki.osdev.org/Interrupt_Vector_Table
	ivt[0x0e] = floppy_handler;    // Install new handler

	for(i=0; i<sizeof buffer; i++)
		buffer[i] = i;

	enter();

	// enable floppy motor
	switch_on();

	printf("* seek to start of floppy\n");
	printf(send_command(cmd_seek, 3, 1, result, 7) == 0 ? "> OK\n": "> failed\n");

	// regular read
	printf("* regular read of 1 sector\n");
	if (send_command(cmd_read, 9, 2, result, 7) == 0) {
		if (result[0] == 0 && result[1] == 0 && result[2] == 0) {
			printf("> OK\n");
			dump(buffer, 23);
		}
		else {
			printf("> failed - ST0: %02x, ST1: %02x, ST2: %02x\n", result[0], result[1], result[2]);
		}
	}
	else {
		printf("> failed\n");
	}

	// disable floppy motor
	switch_off();

	// should fail
	printf("* seek to start of floppy with motor off, should fail\n");
	if (send_command(cmd_seek, 3, 1, result, 7) == 0)
		printf("> failed: seek succeeded\n");
	else
		printf("> OK: seek failed\n");

	// finish
	ivt[0x0e] = oldhandler;    // Install new handler

	printf("> finished\n");
	enter();
}
